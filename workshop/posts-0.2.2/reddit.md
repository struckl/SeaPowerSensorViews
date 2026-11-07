**Title**

R.A.T.S. Sensor Views 0.2.2: the zoom lag is fixed

**Body**

Thanks to everyone who reported the lag. You were right, and it was my mod: zooming in on the map with the sensor lines on made the game crawl, and at high zoom it froze. The lines were drawn far past the edge of the screen, and the closer you zoomed the longer they got. They are now cut to the visible map before they are drawn.

Measured on my PC in the same mission before and after (Sea Power 0.8.7, sensor lines on):

- Zoom 13: from 31-61 fps with half-second stutters to 72-74 fps (+20 % to +135 %), longest frame 0.86 s → 0.05 s
- Zoom 17: froze the game before, now 74-76 fps
- With the lines off the game runs at the same speed as before, so what's left is the cost of drawing the lines themselves

0.2.1 also made the windows and map layers cheaper to draw: with all windows open and the map moving, fps went up about 8 %, and the mod's own work per frame dropped by about 85 %.

Steam updates it automatically. Tested on 0.8.7; the build for the 3D-globe beta is in the package but I haven't tried it in the game yet. Needs [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757).

Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3833019654
Source (MIT): https://github.com/struckl/SeaPowerSensorViews

If it still stutters for you, tell me your mission and roughly how many ships, and attach `BepInEx\LogOutput.log`.
