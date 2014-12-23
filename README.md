OpenSimTide v0.2

This is a INonSharedRegion module that controls the tide on your region. It
also reports the current tide level to the region on two channels so that
scripts can use it, for example to make items appear to float.

If you are running multiple regions on one simulator you can have different tide
settings per region in the configuration file, in the exact same way you can
customize per Region setting in Regions.ini

The configuration file for this module is in:

addon-modules/OpenSimTide/config/OpenSimTide.ini

and follows the same format as a Regions.ini file, where you specify setting for
each region using the [Region Name] section heading.

Here is an example config:

	[Test Region 1]

		;# {TideEnabled} {} {Enable the tide to come in and out?} {true false} false
		;; Tides currently only work on single regions and varregions (non megaregions) 
		;# surrounded completely by water
		;; Anything else will produce wierd results where you may see a big
		;; vertical 'step' in the ocean

		TideEnabled = True

		;; update the tide every x simulator frames
		TideUpdateRate = 50

		;; low and high water marks in metres
		TideLowWater = 17.0
		TideHighWater = 20.0

		;; how long in seconds for a complete cycle time low->high->low et
		TideCycleTime = 900

		;; provide tide information on the console?
		TideInfoDebug = False

		;; chat tide info to the whole region?
		TideInfoBroadcast = True

		;; which channel to region chat on for the full tide info
		TideInfoChannel = 5555

		;; which channel to region chat on for just the tide level in metres
		TideLevelChannel = 5556

		;; How many times to repeat Tide Warning messages at high/low tide
		TideAnnounceCount = 5


To add this module to your OpenSim, cd to your addon-modules directory and type:

git clone https://github.com/JakDaniels/OpenSimTide.git

Rerun the prebuild script in the main opensim directory and rebuild with xbuild or nant.

How do I use the tide data in scripts?
Here is an example script. Rez a spherical prim above the water and place this script in it.
Name the script FloatOnWater and take a copy of it into your inventory for later.

	integer listen_handle;
	vector myPos;
	float tideLevel = 20.0;

	default
	{
	    on_rez(integer start_param)
	    {
		llResetScript();
	    }
	    state_entry()
	    {
		listen_handle = llListen(5556, "TIDE", NULL_KEY, "");
	    }
	    listen( integer channel, string name, key id, string message )
	    {
		tideLevel=(float)message;
		myPos = llGetPos();
		llSetPos(<myPos.x, myPos.y, tideLevel + 0.05>);
	    }   
	}

To make items float on water just place this script into their root prim.

More complex stuff can be done using the full info channel, which has data about
where in the tide cycle we are. Rez a cube prim and place this script inside:

	integer listen_handle;
	default
	{
	    state_entry()
	    {
		listen_handle = llListen(5555, "TIDE", NULL_KEY, "");
	    }
	    listen( integer channel, string name, key id, string message )
	    {
		llWhisper(0,channel + " " + name + " " + id + "\n" + message);
	    }
	}


The cube will whisper info about the current tide position every time the tide is updated.

If you have any question please contact Jak Daniels, jak@ateb.co.uk
