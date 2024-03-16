# steamguard-webhooks
Steamguard en masse isn't fun. This helps.

## Prerequisites
1. A gmail account that steamguard notifications are sent to
2. A google development project with the Gmail API enabled
3. Visual Studio 2022 installed if you want to open the solution, or at least the .NET 8 SDK
4. A discord server and channel that you want to receive notifications in, with a webhook

### With that fun stuff out of the way..

Clone this repo and move somewhere that you won't nuke anytime soon.
Feel free to pop the solution open and build however you want, but if you don't want to open Visual Studio, run the following (replacing `c:\steamguard` with the folder that you cloned the repo to)

```
git clone https://github.com/hewisaurus/steamguard-webhooks.git c:\steamguard

cd c:\steamguard

dotnet publish src/steamguard.sln --framework net8.0 --runtime win-x64 -c Release

mv C:\steamguard\src\Steamguard\bin\Release\net8.0\win-x64\publish\* .
```

Within the project that has the Gmail API enabled, create an OAuth 2.0 Client with the type 'Desktop'.

Using the badly named 'Download OAuth client' button on the far right of the OAuth 2.0 Client IDs table, in the modal that appears, click 'Download JSON'.

Rename the file to something a bit easier on the eyes, and move it into a folder that is unlikely to get obliterated by any reasonable action like clearing your download or temp folders out. Let's say we've just moved this to `C:\steamguard\credentials` and have renamed it to `steamguard_creds.json`.

### Running the app

There are currently `3` required arguments and `2` optional arguments:

#### Required arguments
`credentialFolder` - The path to the folder from which the application will read your downloaded credential file, plus maintain tokens retrieved from the Gmail API

`clientSecretFile` - The filename of the credential file you downloaded earlier, which should be in the folder specified by `credentialFolder`

`discordWebhookUrl` - The webhook URL that the application will send messages to when a steamguard email is detected
#### Optional arguments
`firstrun` - Set this to `yes` to sync the saved message ID file with what's already in the Gmail account (once)

`loopSeconds` - This defaults to 30, and specifies the amount of time (in seconds) between subsequent runs.

Arguments should be specified in the format argument=value, and so the following example would work if the discord webhook was real.
```
C:\steamguard\Steamguard.exe credentialFolder=C:\steamguard\credentials clientSecretFile=steamguard_creds.json discordWebhookUrl=https://discord.com/api/webhooks/somebigstringoftextlivesinhere firstrun=no loopseconds=30
```
