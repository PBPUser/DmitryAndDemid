using Xunit;

// The suite runs one test at a time.
//
// Everything here reads content through Assets.Source, which is a single static seam: TestEnvironment points
// it at the repo before each fixture, and StaticIllustrationTests moves it to a throwaway folder for the life
// of a test. With xUnit's default per-collection parallelism those two are a race — one class repointing the
// seam under another mid-read — and the failure would be an occasional "asset not found" in a test that never
// touched the seam at all.
//
// The whole suite runs in around a tenth of a second, so serialising it costs nothing worth measuring.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
