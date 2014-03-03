using System;
using System.Net;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using log4net;
using Nini.Config;
using OpenSim.Framework;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenMetaverse;
using Mono.Addins;

[assembly: Addin("TideModule", "0.1")]
[assembly: AddinDependency("OpenSim", "0.5")]

namespace TideModule
{
    [Extension(Path = "/OpenSim/RegionModules", NodeName = "RegionModule")]
    public class AvatarServicesModule : ISharedRegionModule
    {
        #region Fields
        private static readonly ILog m_log = LogManager.GetLogger (MethodBase.GetCurrentMethod ().DeclaringType);
        private const int TICKS_PER_SECOND = 10000000;

        public string m_name = "TideModule";
        private uint m_frame = 0;
        private int m_frameUpdateRate = 100;
        private bool m_enabled = false;
        private bool m_ready = false;
         
        private float m_tideLevel = 20.0f;  //current water level in m
        private float m_lowTide = 18.0f;    //low water level in m
        private float m_highTide = 22.0f;   //high water level in m
        private ulong m_cycleTime = 3600;      //low->high->low time in seconds
        private DateTime m_lowTideTime = new DateTime(); // datetime indicating when next low tide will be
        private DateTime m_highTideTime = new DateTime();  // datetime indicating when next hightide will be
        private bool m_tideDirection = true;    // which direction is the tide travelling, 'Coming In'(true) or 'Going Out'(false)
        private bool m_lastTideDirection = true;
        private bool m_tideInfoDebug = false;  //do we chat the tide to the OpenSim console?
        private bool m_tideInfoBroadcast = true; //do we chat the tide to the region?
        private int m_tideInfoChannel = 5555; //chat channel for all tide info
        private int m_tideLevelChannel = 5556; //chat channel for just the tide level in m
        private int m_tideAnnounceCount = 5; //how many times do we announce the turning tide
    
        private Dictionary<string, Scene> m_scenel = new Dictionary<string, Scene> ();

        public IConfigSource m_config;
        public Scene m_world;
        public RegionInfo m_regionInfo;
        public Dictionary<string, Scene> mScene = new Dictionary<string, Scene> ();
        public Vector3 m_shoutPos = new Vector3(128f, 128f, 30f);
        private int m_tideAnnounceCounter = 0; //counter we use to count announcements of low or high tide
        private string m_tideAnnounceMsg = "";
        #endregion

        #region ISharedRegionModule implementation
        public void PostInitialise ()
        {
        }

        #endregion

        #region IRegionModuleBase implementation
        public void Initialise (IConfigSource source)
        {
            m_config = source;
            IConfig cnf = source.Configs["Tide"];
            
            if (cnf == null)
            {
                m_enabled = false;
                m_log.Info ("[TIDE]: No Configuration Found, Disabled");
                return;
            }
            m_enabled = cnf.GetBoolean("enabled", false);
            m_frameUpdateRate = cnf.GetInt("tide_update_rate", 150);
            m_lowTide = cnf.GetFloat("tide_low_water", 18.0f);
            m_highTide = cnf.GetFloat("tide_high_water", 22.0f);
            m_cycleTime = (ulong) cnf.GetInt("tide_cycle_time", 3600);
            m_tideInfoDebug = cnf.GetBoolean("tide_info_debug", false);
            m_tideInfoBroadcast = cnf.GetBoolean("tide_info_broadcast", true);
            m_tideInfoChannel = cnf.GetInt("tide_info_channel", 5555);
            m_tideLevelChannel = cnf.GetInt("tide_level_channel", 5556);
            m_tideAnnounceCount = cnf.GetInt("tide_announce_count", 5);
 
            if (m_enabled)
            {
                m_log.InfoFormat("[TIDE] Enabled with an update rate of {0} frames, Low Water={1}m, High Water={2}m, Cycle Time={3} secs", m_frameUpdateRate, m_lowTide, m_highTide,m_cycleTime);
            	m_frame = 0;

            	// Mark Module Ready for duty
              	m_ready = true;
				return;	
			}						
        }


        public void Close ()
        {
            if (m_enabled) {
                m_ready = false;
                foreach (Scene m_scene in m_scenel.Values)
                {
                    m_scene.EventManager.OnFrame -= TideUpdate;
                }
            }
        }


        public void AddRegion (Scene scene)
        {
            m_log.InfoFormat ("[TIDE]: Adding {0}", scene.RegionInfo.RegionName);
            if (m_enabled == true) {
                if (m_scenel.ContainsKey(scene.RegionInfo.RegionName)) {
                    m_scenel[scene.RegionInfo.RegionName] = scene;
                    m_shoutPos = new Vector3(scene.RegionInfo.RegionSizeX / 2f, scene.RegionInfo.RegionSizeY / 2f, 30f);
                } else {
                    m_scenel.Add(scene.RegionInfo.RegionName, scene);
                    scene.EventManager.OnFrame += TideUpdate;
                }
            }
        }


        public void RemoveRegion (Scene scene)
        {
            if (m_scenel.ContainsKey(scene.RegionInfo.RegionName)) {
                lock (m_scenel) {
                    m_scenel.Remove(scene.RegionInfo.RegionName);
                }
            }
            scene.EventManager.OnFrame -= TideUpdate;
        }


        public void RegionLoaded (Scene scene)
        {
            if (m_enabled == false)
                return;

        }


        public string Name {
            get { return m_name; }
        }


        public Type ReplaceableInterface {
            get { return null; }
        }

        #endregion

        #region TideModule
        // Place your methods here
        public void TideUpdate ()
        {
            ulong timeStamp;
            double cyclePos; //cycles from 0.0000000001 to 0.999999999999
            double cycleRadians;
            double tideRange;
            double tideMiddle;
            string tideLevelMsg;
            int sx;
            int sy;
            
            
        	if (((m_frame++ % m_frameUpdateRate) != 0) || !m_ready) {
                return;
            }
            timeStamp = (ulong) (DateTime.Now.Ticks);

            cyclePos = (double)(timeStamp % (m_cycleTime * TICKS_PER_SECOND)) / (m_cycleTime * TICKS_PER_SECOND);
            cycleRadians = cyclePos * Math.PI * 2;

            if (cyclePos < 0.5) m_tideDirection = false; else m_tideDirection = true;

            if (m_tideDirection != m_lastTideDirection)
            { //if the tide changes re-calculate the tide times
                if (cyclePos < 0.5)
                { // tide just changed to be high->low
                    m_lowTideTime = DateTime.Now.AddSeconds((double)(m_cycleTime * (0.5 - cyclePos)));
                    m_highTideTime = m_lowTideTime.AddSeconds((double)(m_cycleTime / 2));
                    m_tideAnnounceMsg = "High Tide";
                }
                else
                {   //tide just changed to be low->high
                    m_highTideTime = DateTime.Now.AddSeconds((double)(m_cycleTime * (1.0 - cyclePos)));
                    m_lowTideTime = m_highTideTime.AddSeconds((double)(m_cycleTime / 2));
                    m_tideAnnounceMsg = "Low Tide";
                }
                m_lastTideDirection = m_tideDirection;
            }
            tideRange = (double) (m_highTide - m_lowTide) / 2;
            tideMiddle = (double) m_lowTide + tideRange;
            m_tideLevel = (float) (Math.Cos(cycleRadians) * tideRange + tideMiddle);

            tideLevelMsg = "Current Server Time: " + DateTime.Now.ToString("T") + "\n";
            tideLevelMsg += "Current Tide Level: " + m_tideLevel.ToString() + "\n";
            tideLevelMsg += "Low Tide Time: " + m_lowTideTime.ToString("T") + "\n";
            tideLevelMsg += "Low Tide Level: " + m_lowTide.ToString() + "\n";
            tideLevelMsg += "High Tide Time: " + m_highTideTime.ToString("T") + "\n";
            tideLevelMsg += "High Tide Level: " + m_highTide.ToString() + "\n";
            tideLevelMsg += "Tide Direction: " + ((m_tideDirection) ? "Coming In" : "Going Out") + "\n";
            tideLevelMsg += "Cycle Position: " + cyclePos.ToString() + "\n";
            if (m_tideAnnounceMsg != "")
            {
                if (m_tideAnnounceCounter++ > m_tideAnnounceCount)
                {
                    m_tideAnnounceCounter = 0;
                    m_tideAnnounceMsg = "";
                }
                else
                {
                    tideLevelMsg += "Tide Warning: " + m_tideAnnounceMsg + "\n";
                }
            }

            if (m_tideInfoDebug) m_log.InfoFormat("[TIDE] Sea Level currently at {0}m", m_tideLevel);
            foreach (Scene m_scene in m_scenel.Values)
            {
                if (m_tideInfoBroadcast && m_tideDirection)
                {
                    m_scene.SimChat(Utils.StringToBytes(tideLevelMsg), ChatTypeEnum.Region, m_tideInfoChannel, m_shoutPos, "TIDE", UUID.Zero, false);
                    m_scene.SimChat(Utils.StringToBytes(m_tideLevel.ToString()), ChatTypeEnum.Region, m_tideLevelChannel, m_shoutPos, "TIDE", UUID.Zero, false);
                }   
                if (m_tideInfoDebug) m_log.InfoFormat("[TIDE] Updating Region: {0}", m_scene.RegionInfo.RegionName);
                m_scene.RegionInfo.RegionSettings.WaterHeight = m_tideLevel;
                m_scene.EventManager.TriggerRequestChangeWaterHeight(m_tideLevel);
                m_scene.EventManager.TriggerTerrainTick();
                if (m_tideInfoBroadcast && !m_tideDirection)
                {
                    m_scene.SimChat(Utils.StringToBytes(tideLevelMsg), ChatTypeEnum.Region, m_tideInfoChannel, m_shoutPos, "TIDE", UUID.Zero, false);
                    m_scene.SimChat(Utils.StringToBytes(m_tideLevel.ToString()), ChatTypeEnum.Region, m_tideLevelChannel, m_shoutPos, "TIDE", UUID.Zero, false);
                }
            }

        }
        #endregion
    }
}
