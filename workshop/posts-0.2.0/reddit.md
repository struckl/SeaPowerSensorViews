**Title**

New mod: R.A.T.S. Sensor Views shows which of your units sees which contact, and with which sensor

**Body**

I kept losing track of which ship actually holds a contact and with what. So I built three windows that answer that, in the game's own window style, and two layers for the tactical map. Everything is display only and uses the track estimates your sensors already report, never a contact's real position.

**What it adds**

- Sensor board: your units across, contacts down, a letter per sensor that holds the contact (R F E S A V M) and the age of the freshest fix, with a bearing history strip for bearing-only contacts
- Radar scope: contacts around the selected unit, blind arcs, radar horizon against a target altitude, ESM and sonar bearing lines, RWR and jammer strobes, fire-control LOCK, a measuring cursor
- Sonar display: waterfall relative to the bow, baffle sector, own noise, a depth strip with the layer and the contacts' estimated depth, a measuring cursor and an echo plot
- Map layers: sensor lines from each unit to what each sensor sees, and an optional coverage layer with terrain shadows
- Buttons in the bottom bar and hot keys, windows pop out with Pop-Out Windows

**Where the data comes from** Only what your own sensors report to the game's plotting table: track bearing, range, depth and age estimates. It changes nothing in a mission, the mission's own rules decide what a sensor can detect. Passive sonar depth is the game's own rough estimate.

**Game version** Run in the game on Sea Power 0.8.7. The package also holds a build for the 3D globe beta; I have not tried that one in the game yet. Needs Anchor Chain.

Needs [Anchor Chain](https://steamcommunity.com/sharedfiles/filedetails/?id=3380210757). Workshop: https://steamcommunity.com/sharedfiles/filedetails/?id=3833019654
Source (MIT): https://github.com/struckl/SeaPowerSensorViews

If something looks off, tell me and attach `BepInEx\LogOutput.log` with the window and unit you used.

---

Gallery, in this order (the caption goes into each image's caption field):

| # | File | Caption |
| --- | --- | --- |
| 1 | `board-and-scope.jpg` | The sensor board and the radar scope: who holds what, and where |
| 2 | `sonar-display.jpg` | Sonar display: waterfall relative to the bow, and the depth strip with the contacts' estimated depth |
| 3 | `sonar-echoes.jpg` | The Echoes button: contacts by bearing and range |
| 4 | `map-sensor-lines-coverage.jpg` | On the map: a line per sensor to its contact, and the selected unit's coverage |
| 5 | `map-zoomed-out.jpg` | Zoomed out: radar and sonar coverage of the selected ship, with the lines to every contact |
