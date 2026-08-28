using arch_forge_backend.Application.Localization;

namespace arch_forge_backend.Application.Analysis.Rules;

public interface IArchitectureRule
{
    string Code { get; }
    Area Area { get; }
    Severity DefaultSeverity { get; }
    RulePhase Phase { get; }

    /// <summary>
    /// True for the rules that talk about the <em>drawing</em> and not about the architecture.
    ///
    /// V13 §11 asked for these to come out before everything else and offered three shapes for it —
    /// an area, a severity, or a mark. V21 measured the area and it was the wrong one: the two rules
    /// never fire on any reference, so an area holding them would score 100 everywhere and lift every
    /// score for free. This is the third shape, and it is the only one that costs no points: while a
    /// drawing error is on screen, <strong>the rest of the panel describes a system that is not
    /// yours</strong>, so it is worth reading first and worth nothing in the score.
    /// </summary>
    bool DescribesTheDrawing => false;

    LocalizedText PassedMessage { get; }

    LocalizedText PassedExplanation { get; }

    RuleResult Evaluate(ArchitectureGraph graph);
}
