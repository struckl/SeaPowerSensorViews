# How the windows are hosted and styled

Researched 2026-11-02 from the game's own XAML (extracted from the Unity assets of the installed game, 0.8.7 build; the globe
build's XAML was not available offline) and the decompiled `SeapowerUI` sources.

## Hosting (a real game window, no overlay)

- `MainGameView` has a `SeapowerUI.WindowHost` behaviour; `WindowHost.Windows` is an `ObservableCollection<object>` shown by an
  `ItemsControl` whose items are `ContentControl`s. A window is a *content object* (a view model) put into that collection with
  `WindowHostCommands.OpenWindowCommand.Execute(content, anyElement)`; the game closes it with `CloseWindowCommand`.
- The content is drawn through an implicit `DataTemplate` found by the content's type. The game's templates (for example
  `SensorInfoViewTemplates.xaml`) are `<local:Window Template="{StaticResource WindowLayoutNoScroll}" ...>` with a `Header`.
  `Window.xaml` supplies the chrome: header bar (`Brush.Window.Header`), body (`Brush.Window.Background`), close button, resize thumbs.
- The mod defines three public content classes (`BoardWindow`, `ScopeWindow`, `SonarWindow`), parses one `ResourceDictionary` with a
  `DataTemplate` per class (`Ui/Kit.cs`) and merges it into `MainGameView.Resources`. Noesis finds the mod's types by name through
  `Extend.FindType` (all loaded assemblies). The template uses `{DynamicResource WindowLayoutNoScroll}` because the key lives in the
  main view's dictionaries, not in the parsed one. If that attribute fails to parse, the default `Window` style (with a scroll area) is used.
- Pop-Out Windows keys on `content.GetType().Name` and flattens `MainGameView.Resources` when it builds its screen, so the new windows
  get its pop-out button and remembered place with no code on either side. The content object stays the same when a window moves, so view
  state lives in the content class.

## Styling

- Controls (CheckBox, Slider, Button, TextBlock) are the stock ones; the game's implicit styles (`NoesisTheme.Styles`) apply.
- Custom drawing (`Ui/Surface.cs`) uses `UserControl.OnRender` with a `DrawingContext`, like the game's `MapControl` classes. Brushes are
  taken from the game's resources by key at load: `Brush.Window.BackgroundDark`, `Brush.Window.Header`, `Brush.Border.Normal`,
  `Brush.Foreground.Normal/Highlight/Emphasis`, `Brush.Glyph.SeaPowerRed/Green/Blue`, `NTDS.HostileColour/FriendColour/NeutralColour/UnknownColour`,
  font `Font.Family.Default`. The literals in `Surface.Theme` are only the fallback (same values as the dark theme).
- Bottom bar buttons: `ToggleButton` with the game's `SimpleToggleButton` style and a `Brush.Item.BackgroundOver` hover, like the
  Standing Orders button; lit = text in `Brush.Foreground.Emphasis`. A sixth column is appended to `DisplayAreaGrid`.

## Fallback

If the template cannot be registered the mod logs it and the hot keys do nothing; the planned fallback would be Noesis elements laid over
the map as Topo's `Ui/MapDecoration.cs` does. Not needed unless the in-game test shows the window route failing.

## One DLL

Nothing the mod references differs between the two games except `SonarAcoustics.OwnNoiseDB` (four optional parameters added in the globe
build), which is bound by reflection in `Compat.cs`. `python tools/modtool.py compat sensorviews`: 0 missing on either game.
