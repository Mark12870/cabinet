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
    ["scenario-diagnosis", .. var artifacts] when artifacts.Length > 0 => await ScenarioDiagnosis.Diagnose(artifacts),
    ["scenario-shards", var previous, var count] when int.TryParse(count, out var shards) && shards > 0 =>
        ScenarioShards.Plan(previous, shards),
    ["vendor-streaks", var previous, var output, .. var artifacts] => VendorStreaks.Update(previous, output, artifacts),
    ["ci-timings", var repository, var count] when int.TryParse(count, out var runs) && runs > 0 =>
        await CiTimings.Report(repository, runs),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: Cabinet.Ci scenario-report [--passed] TRX | scenario-diagnosis ARTIFACT... | scenario-shards PREVIOUS COUNT | vendor-streaks PREVIOUS OUTPUT ARTIFACT... | scenario-approve TRX ID | catalogue-updates discover|check|coverage OUTPUT | catalogue-updates validate BASE IDS_JSON_FILE | catalogue-updates release BASE IDS_JSON_FILE DATE | ci-timings OWNER/REPO COUNT");
    return 2;
}
