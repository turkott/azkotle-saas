// Integrační testy spouští Postgres + MinIO Testcontainers per IClassFixture.
// Paralelní běh napříč 7 fixturami zahltí Docker (race conditions, padá MinIO startup).
// Serial běh trvá ~30s, paralelní by ušetřil pár sekund za cenu flakiness.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
