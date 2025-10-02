using Azure.Provisioning.ContainerRegistry;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddAzureContainerAppEnvironment("env")
    .ConfigureInfrastructure(infra =>
    {
        var acr = infra.GetProvisionableResources().OfType<ContainerRegistryService>().First();
        acr.Sku = new() { Name = ContainerRegistrySkuName.Standard };
        acr.IsAnonymousPullEnabled = true;
    });

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator()
    .ConfigureInfrastructure(infra =>
    {
        var sa = infra.GetProvisionableResources().OfType<StorageAccount>().FirstOrDefault()!;
        sa.AllowBlobPublicAccess = false;
    });

builder.AddAzureFunctionsProject<Projects.FunctionApp18>("functionapp18")
       .WithHostStorage(storage)
       .WithExternalHttpEndpoints()
       .PublishWithContainerAppSecrets();

builder.Build().Run();