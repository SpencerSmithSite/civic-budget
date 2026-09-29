using CivicBudget.ReferenceErp;

// Run it with a key of your choosing, then point a CivicBudget connection at it:
//   dotnet run --project samples/CivicBudget.ReferenceErp -- --urls http://localhost:5090 --ReferenceErp:ApiKey=local-test-key
await ReferenceErpServer.Build(args).RunAsync();
