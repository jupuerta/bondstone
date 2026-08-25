namespace arch_forge_backend.Application.Analysis;

/// <summary>
/// V16 C7a. What one rule did over this architecture, in the three states the user can act on.
///
/// The distinction is <see cref="RuleResult"/>'s, and it has existed since V2 D5.2: a rule answers
/// "applicable, and here is what I found" or "there was nothing here to check", which is why
/// <see cref="AnalysisSummary.Passed"/> can count checks that were actually run instead of
/// rewarding an architecture too small to check. What was missing is that the distinction was
/// spent on the score and then dropped - <see cref="Scoring.RuleOutcome"/> carries it as far as
/// <see cref="Scoring.ScoreCalculator"/> and no further - so the user could see the two findings a
/// rule made and never that twenty-two other rules had run, nor which of them had gone quiet
/// because the drawing does not say enough yet.
/// </summary>
public enum RuleState
{
    /// <summary>It ran and found something: its findings are in <c>Findings</c>, under this code.</summary>
    Fired,

    /// <summary>
    /// It ran and found nothing - one of the <see cref="AnalysisSummary.Passed"/>, and a
    /// <see cref="Severity.Good"/> finding of its own.
    /// </summary>
    Passed,

    /// <summary>
    /// It never got to look: there was nothing of its kind here (V2 D1.4). It costs the area
    /// nothing and it is not a pass - "no queue is drawn" is not "the queue is fine".
    /// </summary>
    NotApplicable
}

/// <summary>
/// V16 C7a. One line per registered rule, whatever it had to say - which is what makes a catalogue
/// of the twenty-four something the user reads <em>against their own drawing</em> instead of a
/// list of names.
///
/// <strong>Why it is worth the bytes.</strong> A rule, until this shipped, only existed when it
/// fired: nothing in the product said that <c>CACHE_AS_STORE</c> or <c>IMPLAUSIBLE_DEPENDENCY</c>
/// are there at all, and <see cref="RuleState.NotApplicable"/> - the most valuable of the three -
/// was invisible by construction. It is the state that says what the drawing is missing for the
/// product to be able to have an opinion.
///
/// <strong>And the reason it did not apply</strong> - "you have not declared any load", "there is
/// no queue drawn" - is V16 C7b, written rule by rule where each one stands down and carried here
/// unchanged. It is what turns the third state from a shrug into the one line on this screen that
/// tells the user what to draw next.
/// </summary>
/// <param name="Code">The rule's code, the same string <see cref="RuleFinding.Code"/> carries.</param>
/// <param name="Area">The area it feeds. A rule belongs to exactly one (D1.2).</param>
/// <param name="State">Which of the three things it did here.</param>
/// <param name="Reason">
/// V16 C7b. Why it had nothing to look at, as a catalogue key and its arguments like every other
/// sentence (V7 D1.1). Null on <see cref="RuleState.Fired"/> and <see cref="RuleState.Passed"/> -
/// a rule that ran has its findings to speak with - and never null on
/// <see cref="RuleState.NotApplicable"/>, which <see cref="ArchitectureAnalyzer"/> enforces at the
/// only place a rule can stand down. Optional on the wire all the same: a payload recorded before
/// C7b does not carry it, and a card without it is a card without a second line.
/// </param>
public sealed record RuleCoverage(
    string Code,
    Area Area,
    RuleState State,
    Localization.LocalizedText? Reason = null);
