using System;
using System.Collections.Generic;

namespace Jacquard {

// Values rather than mutable shared patches: editing an applied sound must not
// change the starting point offered to another channel. Normal score saving
// already persists all these parameters, so no file version is needed.
public static class TimbrePresets
{
    public static IReadOnlyList<string> Names { get; } = Array.AsReadOnly(new[]
        { "Sine", "Hollow", "Bass", "Bell", "Keys", "Pad", "Kick", "Metal" });

    public static FmPatch Create(string name)
    {
        var p = FmPatch.Default;
        switch (name)
        {
            case "Sine": break;
            case "Hollow":
                p.modulatorRatio = 3; p.modulationIndex = 1;
                p.modulatorDecay = 0.5f; p.carrierRelease = 0.1f; break;
            case "Bass":
                p.modulatorRatio = 1; p.modulationIndex = 2.5f;
                p.modulatorDecay = 0.7f; p.carrierRelease = 0.08f; break;
            case "Bell":
                p.modulatorRatio = 3.5f; p.modulationIndex = 3;
                p.modulatorDecay = 0.3f; p.carrierRelease = 1.2f;
                p.level = -12; p.reverbSend = 0.25f; break;
            case "Keys":
                p.modulatorRatio = 2; p.modulationIndex = 1.5f;
                p.modulatorDecay = 0.65f; p.carrierRelease = 0.25f; break;
            case "Pad":
                p.modulatorRatio = 1; p.modulationIndex = 0.6f;
                p.modulatorDecay = 0; p.carrierAttack = 0.4f;
                p.carrierRelease = 0.8f; p.unison = 0.5f;
                p.level = -12; p.reverbSend = 0.3f; break;
            case "Kick":
                p.pitchSweep = 3; p.pitchDecay = 0.12f;
                p.carrierRelease = 0.15f; p.gateScale = 0.25f; break;
            case "Metal":
                p.modulatorRatio = 7.1f; p.modulationIndex = 5;
                p.feedback = 0.8f; p.modulatorDecay = 0.6f;
                p.carrierRelease = 0.2f; p.level = -12; break;
            default: throw new ArgumentException("Unknown timbre preset: " + name);
        }
        return p;
    }
}

} // namespace Jacquard
