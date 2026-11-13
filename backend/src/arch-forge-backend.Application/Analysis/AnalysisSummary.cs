namespace bondstone_backend.Application.Analysis;

// `Info` is its own number and not folded into `Passed`: a rule that passed found nothing to say,
// and one that reports `Info` found something and decided it was not worth points. Telling the reader
// they are the same thing is the mistake the level exists to undo.
public sealed record AnalysisSummary(int Critical, int Warning, int Info, int Passed);
