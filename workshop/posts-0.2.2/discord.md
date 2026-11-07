**R.A.T.S. Sensor Views 0.2.2** is out: the zoom lag is fixed 🔧

Zooming in on the map with the sensor lines on made the game crawl, and at high zoom it froze. The lines were drawn far past the edge of the screen; now they are cut to the visible map. Measured before/after in the same mission (0.8.7):
- **Zoom 13:** 31-61 fps → 72-74 fps, longest frame 0.86 s → 0.05 s
- **Zoom 17:** froze the game → 74-76 fps
- 0.2.1 already gave about +8 % fps with all windows open and the map moving

Too much on the map? The sensor lines can be switched off in the map's Layers menu (or Ctrl+Shift+PageUp), the windows keep working. Steam updates it automatically; still needs Anchor Chain.
Thanks to everyone who reported it!
<https://steamcommunity.com/sharedfiles/filedetails/?id=3833019654>
