// The CDK's JSII runtime is one Node process per test host and cannot be started twice at once, so
// these tests run one class at a time. They are fast; the synth itself is the slow part.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
