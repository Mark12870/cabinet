using Cabinet.Ci;

return args switch
{
    ["catalogue-updates", "discover", var output] => await CatalogueUpdates.Discover(output),
    ["catalogue-updates", "check", var output] => await CatalogueUpdates.Discover(output, createCandidates: false),
    ["catalogue-updates", "coverage", var output] => CatalogueUpdates.Coverage(output),
    ["catalogue-updates", "validate", var baseline, var idsFile] => CatalogueUpdates.Validate(baseline, idsFile),
    ["catalogue-updates", "release", var baseline, var idsFile, var date] => CatalogueUpdates.Release(baseline, idsFile, date),
    ["scenario-approve", var trx, var id] => ScenarioReport.Approve(trx, id),
    ["scenario-report", "--passed", var trx] => ScenarioReport.Passed(trx),
    ["scenario-report", var trx] => ScenarioReport.Summarise(trx),
    ["scenario-diagnosis", var artifact] => await ScenarioDiagnosis.Diagnose(artifact),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: Cabinet.Ci scenario-report [--passed] TRX | scenario-diagnosis ARTIFACT | scenario-approve TRX ID | catalogue-updates discover|check|coverage OUTPUT | catalogue-updates validate BASE IDS_JSON_FILE | catalogue-updates release BASE IDS_JSON_FILE DATE");
    return 2;
}
