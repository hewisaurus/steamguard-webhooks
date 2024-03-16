using Google.Apis.Auth.OAuth2;
using Google.Apis.Gmail.v1;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace Steamguard;

public static class GmailApiHelper
{
    public static GmailService? GetService(string credentialPath, string googleCredentialStoreFolder)
    {
        try
        {
            // TODO: Wrap this in some sort of thread that has a timeout, if we are concerned about it in the future

            UserCredential credential;
            using (FileStream stream = new FileStream(credentialPath, FileMode.Open, FileAccess.Read))
            {
                credential = GoogleWebAuthorizationBroker.AuthorizeAsync(
                    GoogleClientSecrets.FromStream(stream).Secrets,
                    new List<string>() { "https://mail.google.com/" },
                    "user",
                    CancellationToken.None,
                    new FileDataStore(googleCredentialStoreFolder, true)).Result;
            }
            // Create Gmail API service.
            GmailService service = new GmailService(new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "steamguardinteraction",
            });
            return service;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception thrown while trying to instantiate the Gmail service: {0}", ex.Message);
            return null;
        }

    }
}
