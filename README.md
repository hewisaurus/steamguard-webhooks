# steamguard-webhooks
Steamguard en masse isn't fun. This helps.

## Prerequisites
1. A gmail account that steamguard notifications are sent to
2. A google development project with the Gmail API enabled

### With that fun stuff out of the way..
Within the project that has the Gmail API enabled, create an OAuth 2.0 Client with the type 'Desktop'.

Using the badly named 'Download OAuth client' button on the far right of the OAuth 2.0 Client IDs table, in the modal that appears, click 'Download JSON'.

Rename the file to something a bit easier on the eyes, and move it into a folder that is unlikely to get obliterated by any reasonable action like clearing your download or temp folders out. Let's say we've just moved this to `C:\steamguard\credentials` and have renamed it to `steamguard_creds.json`.

Download the latest release, extract into your folder of choice. Let's assume that I've extracted this into `C:\steamguard`.

