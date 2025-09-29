using AspireApp18.AppHost;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);

// --- BEGIN: Added by me --- //
builder.AddAzureContainerAppEnvironment("env");

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator()
    .ConfigureInfrastructure(infra =>
    {
        var sa = infra.GetProvisionableResources().OfType<StorageAccount>().FirstOrDefault()!;
        sa.AllowBlobPublicAccess = false;
    });
// --- END: Added by me --- //

builder.AddAzureFunctionsProject<Projects.FunctionApp18>("functionapp18")
// --- BEGIN:  Added by me --- //
       .WithHostStorage(storage)
       .WithExternalHttpEndpoints()
       .PublishFunctionsProjectAsAzureContainerApp();
// --- END: Added by me --- //

builder.Build().Run();