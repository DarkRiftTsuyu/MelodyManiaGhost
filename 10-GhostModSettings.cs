using System.Collections.Generic;

// Mod settings. Public fields are saved to / loaded from the mod's settings file by the game.
public class GhostModSettings : IModSettings
{
    public bool enableGhost = true;
    public bool enableRecording = true;
    public bool showScoreDifference = true;
    public bool showPitchGhost = true;
    public bool debugLogging = false;

    public List<IModSettingControl> GetModSettingControls()
    {
        return new List<IModSettingControl>()
        {
            new BoolModSettingControl(() => enableGhost, newValue => enableGhost = newValue) { Label = "Show ghost of personal best" },
            new BoolModSettingControl(() => enableRecording, newValue => enableRecording = newValue) { Label = "Record runs and save personal best" },
            new BoolModSettingControl(() => showScoreDifference, newValue => showScoreDifference = newValue) { Label = "Show score difference" },
            new BoolModSettingControl(() => showPitchGhost, newValue => showPitchGhost = newValue) { Label = "Show pitch ghost" },
            new BoolModSettingControl(() => debugLogging, newValue => debugLogging = newValue) { Label = "Debug logging" },
        };
    }
}
