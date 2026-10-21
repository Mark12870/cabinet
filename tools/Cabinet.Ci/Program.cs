using Cabinet.Ci;

return args switch
{
    ["catalogue-updates", "discover", var output] => await CatalogueUpdates.Discover(output),
    ["catalogue-updates", "check", var output] => await CatalogueUpdates.Discover(output, createCandidates: false),
    ["catalogue-updates", "coverage", var output] => CatalogueUpdates.Coverage(output),
    ["catalogue-updates", "validate", var baseline, var id] => CatalogueUpdates.Validate(baseline, id),
    ["catalogue-updates", "release", var baseline, var id, var date] => CatalogueUpdates.Release(baseline, id, date),
    ["scenario-approve", var trx, var id] => ScenarioReport.Approve(trx, id),
    ["scenario-report", "--passed", var trx] => ScenarioReport.Passed(trx),
    ["scenario-report", var trx] => ScenarioReport.Summarise(trx),
    ["scenario-diagnosis", var artifact] => await ScenarioDiagnosis.Diagnose(artifact),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: Cabinet.Ci scenario-report [--passed] TRX | scenario-diagnosis ARTIFACT | scenario-approve TRX ID | catalogue-updates discover|check|coverage OUTPUT | catalogue-updates validate BASE ID | catalogue-updates release BASE ID DATE");
    return 2;
}
