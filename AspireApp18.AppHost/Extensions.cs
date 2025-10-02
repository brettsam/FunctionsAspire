using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;

namespace Aspire.Hosting;

internal static class Extensions
{
    public static IResourceBuilder<T> PublishWithContainerAppSecrets<T>(this IResourceBuilder<T> builder, params string[] additionalSecretsToCreate)
       where T : AzureFunctionsProjectResource
    {
        if (!builder.ApplicationBuilder.ExecutionContext.IsPublishMode)
        {
            return builder;
        }

        builder.WithEnvironment("AzureWebJobsSecretStorageType", "ContainerApps");

        List<IAzureKeyVaultSecretReference> secrets = [];
        IResourceBuilder<AzureKeyVaultResource>? keyVault = builder.ApplicationBuilder.AddAzureKeyVault("key-vault");

        // Always create a default key        
        foreach (string secretToCreate in additionalSecretsToCreate.Append("host-function-default"))
        {
            var secret = CreateSecretIfNotExists(builder.ApplicationBuilder, keyVault, secretToCreate);
            secrets.Add(secret);
        }

        return builder.PublishAsAzureContainerApp((infra, app) => ConfigureFunctionsContainerApp(infra, app, builder.Resource, secrets.ToArray()));
    }

    private static void ConfigureFunctionsContainerApp(AzureResourceInfrastructure infrastructure, ContainerApp containerApp, IResource resource,
        params IAzureKeyVaultSecretReference[] secrets)
    {
        const string volumeName = "functions-keys";
        const string mountPath = "/run/secrets/functions-keys";

        var appIdentityAnnotation = resource.Annotations.OfType<AppIdentityAnnotation>().Last();
        var containerAppIdentityId = appIdentityAnnotation.IdentityResource.Id.AsProvisioningParameter(infrastructure);

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

                // Deploy the secret only if it does not already exist
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
