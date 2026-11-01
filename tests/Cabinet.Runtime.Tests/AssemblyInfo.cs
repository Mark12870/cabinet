using System.Runtime.Versioning;
using Xunit.Sdk;
using Xunit.v3;

[assembly: SupportedOSPlatform("linux")]
[assembly: Parallelization(Mode = ParallelMode.None)]
[assembly: TestCollectionOrderer(typeof(Cabinet.Runtime.Tests.Scenarios.LongestFirst))]
