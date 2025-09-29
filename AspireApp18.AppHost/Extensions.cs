using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;

namespace AspireApp18.AppHost;

internal static class Extensions
{
    public static IResourceBuilder<T> PublishFunctionsProjectAsAzureContainerApp<T>(this IResourceBuilder<T> builder)
       where T : AzureFunctionsProjectResource
    {
        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        var keyVault = builder.ApplicationBuilder.AddAzureKeyVault("key-vault");
        var secret = Extensions.CreateSecretIfNotExists(builder.ApplicationBuilder, keyVault, "host-function-a");

        builder.WithEnvironment("AzureWebJobsSecretStorageType", "ContainerApps");
        builder.PublishAsAzureContainerApp((infra, containerApp) => ConfigureFunctionsContainerApp(infra, containerApp, builder.Resource, secret));

        return builder;
    }

    private static void ConfigureFunctionsContainerApp(AzureResourceInfrastructure infrastructure, ContainerApp containerApp, IResource resource,
        params IAzureKeyVaultSecretReference[] secrets)
    {
        const string volumeName = "functions-keys";
        const string mountPath = "/run/secrets/functions-keys";

        ProvisioningParameter containerAppIdentityId;
        if (resource.TryGetLastAnnotation<AppIdentityAnnotation>(out var appIdentityAnnotation))
        {
            var appIdentityResource = appIdentityAnnotation.IdentityResource;
            containerAppIdentityId = appIdentityResource.Id.AsProvisioningParameter(infrastructure);
        }
        else
        {
            throw new InvalidOperationException($"The resource {resource.Name} must have an AppIdentityAnnotation to use Key Vault secrets.");
        }

        var containerAppSecretsVolume = new ContainerAppVolume
        {
            Name = volumeName,
            StorageType = ContainerAppStorageType.Secret
        };

        foreach (var secretReference in secrets)
        {
            var secret = secretReference.AsKeyVaultSecret(infrastructure);

            var containerAppSecret = new ContainerAppWritableSecret()
            {
                Name = secretReference.SecretName,
                KeyVaultUri = secret.Properties.SecretUri,
                Identity = containerAppIdentityId
            };

            containerApp.Configuration.Secrets.Add(containerAppSecret);

            // secret paths use '.', but secrets can only use '-'
            var secretPath = secretReference.SecretName.Replace("-", ".").ToLowerInvariant();

            containerAppSecretsVolume.Secrets.Add(new SecretVolumeItem
            {
                Path = secretPath,
                SecretRef = secretReference.SecretName
            });
        }

        var containerAppSecretsVolumeMount = new ContainerAppVolumeMount
        {
            VolumeName = volumeName,
            MountPath = mountPath
        };

        // TODO: will there always be 1 Container?
        containerApp.Template.Containers[0].Value!.VolumeMounts.Add(containerAppSecretsVolumeMount);
        containerApp.Template.Volumes.Add(containerAppSecretsVolume);
    }


    public static IAzureKeyVaultSecretReference CreateSecretIfNotExists(IDistributedApplicationBuilder builder, IResourceBuilder<AzureKeyVaultResource> keyVault, string secretName)
    {
        var secretParameter = ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, $"param-{secretName}", special: false);

        builder.AddBicepTemplateString($"key-vault-key-{secretName}", """
                param location string = resourceGroup().location
    
                param keyVaultName string

                param secretName string

                @secure()
                param secretValue string    

                // Reference the existing Key Vault
                resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
                  name: keyVaultName
                }

                // Deploy the secret only if it doesn’t already exist
                @onlyIfNotExists()
                resource newSecret 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
                  parent: keyVault
                  name: secretName
                  properties: {
                      value: secretValue
                  }
                }
                """)
            .WithParameter("keyVaultName", keyVault.GetOutput("name"))
            .WithParameter("secretName", secretName)
            .WithParameter("secretValue", secretParameter);

        return keyVault.GetSecret(secretName);
    }
}
