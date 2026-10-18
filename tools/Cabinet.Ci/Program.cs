using Cabinet.Ci;

return args switch
{
    ["scenario-report", "--passed", var trx] => ScenarioReport.Passed(trx),
    ["scenario-report", var trx] => ScenarioReport.Summarise(trx),
    ["scenario-diagnosis", var artifact] => await ScenarioDiagnosis.Diagnose(artifact),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("usage: Cabinet.Ci scenario-report [--passed] TRX | scenario-diagnosis ARTIFACT");
    return 2;
}
