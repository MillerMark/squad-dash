namespace SquadDash.GuidedTours;

internal enum TypeIntoPromptMode
{
    /// <summary>Types text into the prompt box; leaves it for the user to send.</summary>
    Draft,
    /// <summary>Types text into the prompt box and enqueues it as a sim item (no AI call).</summary>
    Sim,
}
