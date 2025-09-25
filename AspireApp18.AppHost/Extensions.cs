using Aspire.Hosting.Azure;
using Azure.Provisioning;
using Azure.Provisioning.AppContainers;

namespace AspireApp18.AppHost;

internal static class Extensions
{
    public static IResourceBuilder<T> PublishFunctionsProjectAsAzureContainerApp<T>(this IResourceBuilder<T> builder, IAzureKeyVaultSecretReference secret)
       where T : AzureFunctionsProjectResource
    {
        builder.WithEnvironment("AzureWebJobsSecretStorageType", "ContainerApps");

        builder.PublishAsAzureContainerApp((infra, containerApp) => ConfigureFunctionsContainerApp(infra, containerApp, builder.Resource, secret));

        return builder;
    }

    static void ConfigureFunctionsContainerApp(AzureResourceInfrastructure infrastructure, ContainerApp containerApp, IResource resource,
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
}
