using System.Net.Http.Json;
using Google.Apis.Gmail.v1;
using Google.Apis.Gmail.v1.Data;
using Steamguard.Models;

namespace Steamguard;

public class Actions
{
    private HttpClient _client;

    public Actions()
    {
        _client = new HttpClient();
    }

    public SteamGuardAccountCode? GetSteamGuardCode(Message message, string searchText, bool showDebuggingMessages = false)
    {
        // Get the plain text goodness, cause it's gonna be no fun to look through the HTML.
        // Note to myself: I wasted a lot of time trying. Don't try it again in the hope that steam have made it better. Just don't.

        var plainTextPart = message.Payload.Parts.FirstOrDefault(p => p.MimeType == "text/plain");
        if (plainTextPart == null)
        {
            // Didn't find a plain text version, so shouldn't be a steam message, unless they've changed how they send email
            if (showDebuggingMessages)
            {
                Console.WriteLine("Skipping {0} as there's no plain text version inside the message", message.Id);
            }

            return null;
        }

        if (string.IsNullOrEmpty(plainTextPart.Body.Data))
        {
            if (showDebuggingMessages)
            {
                Console.WriteLine("Skipping {0} as the plain text content appears to be null, which is weird.", message.Id);
            }

            return null;
        }

        var originalBodyB64 = plainTextPart.Body.Data;
        // Fix google doing google things. In case of emergency, find gin.
        // Gratefully inspired by https://stackoverflow.com/questions/63879748/decode-googles-base64
        var betterBodyB64 = originalBodyB64.Replace('-', '+').Replace('_', '/')
                            + (originalBodyB64.Length % 4) switch
                            {
                                2 => "==",
                                3 => "=",
                                _ => "",
                            };

        byte[] b64decodedBytes = Convert.FromBase64String(betterBodyB64);
        // Shouldn't need the uppercase conversion but let's not let future case sensitivity spoil all of our fun
        var decodedText = System.Text.Encoding.UTF8.GetString(b64decodedBytes).ToUpper();

        if (!decodedText.Contains(searchText))
        {
            if (showDebuggingMessages)
            {
                Console.WriteLine("Skipping {0} as we didn't find our magic string, which is currently \"{1}\"", message.Id, searchText);
            }
            return null;
        }

        var messageLines = decodedText.Split("\r\n").Where(s => !string.IsNullOrEmpty(s)).ToList();

        /*
         * Rules on how we find what we want, after we've removed empty lines.
         * Rule for account name:
         *
         * The first line should be "Dear <steam account name>, e.g. "DEAR KICKASSACCOUNT001,"
         * and so getting the account is straightforward. If we really want, check each line for this content.
         *
         * Rules for code:
         *
         * In 2023 as I hacked this together, I was previously searching for the text "Request made from Australia" but clearly
         * that isn't scalable. There are two lines where we can find this stuff, usually #4 and #5.
         * e.g. Line 4: "REQUEST MADE FROM"
         *      Line 5: "WASHINGTON, UNITED STATES"
         * Line length and text is clearly going to change per region so we can't use this anymore.
         *
         * I **think** that line 6 "LOGIN CODE" always shows up the same, as does line 7
         * which is the code - a 5 character code "CK2N6" for example
         *
         * Rule #1: Check for a 5 character string on line 7. Continue if we don't have a 5 char string
         * Rule #2: Check for the location of the string "LOGIN CODE", grab the line number, and use the value of the following line for the code
         * Rule #3: Look for the text "IF THIS WASN'T YOU", grab the line number, and use the value of the preceding line for the code
         * Rule #4: Spit some passive aggressive log message out somewhere and whine about not being able to reasonably locate the steamguard code.
         * 
         */

        // We shouldn't be having to do this too often, so don't worry about iterating over the list multiple times if we want. We're not that precious.

        if (messageLines.Count < 10)
        {
            // 10 is arbitrary, we really know that we shouldn't need more than 7 lines based on what we've seen in the past
            if (showDebuggingMessages)
            {
                Console.WriteLine("Skipping {0} as we didn't find enough content in the body.. only found {1} lines.", message.Id, messageLines.Count);
            }
            return null;
        }

        var accountNameLine = messageLines.FirstOrDefault(l => l.Contains("DEAR"));
        if (string.IsNullOrEmpty(accountNameLine))
        {
            Console.WriteLine("Ok, this is a problem. Message {0} is a steamguard email but we have no idea where the account name content is.", message.Id);
            // TODO: Flag that there's an issue... somehow
            return null;
        }

        // Don't assume the format is always the same... so sanity check things that we shouldn't have to. heh.
        var accountName = accountNameLine.Replace("DEAR", "").Trim();
        if (accountName.EndsWith(","))
        {
            // Rip the last character off the string
            accountName = accountName[..^1];
        }

        var code = string.Empty;
        // Rule 1
        var line7 = messageLines[6].Trim();
        if (line7.Length == 5)
        {
            code = line7;
        }

        // Rule 2
        if (string.IsNullOrEmpty(code))
        {
            for (var idx = 0; idx < messageLines.Count; idx++)
            {
                if (messageLines[idx].Trim().Contains("LOGIN CODE"))
                {
                    code = messageLines[idx + 1].Trim();
                    break;
                }
            }
        }

        // Rule 3
        if (string.IsNullOrEmpty(code))
        {
            for (var idx = 0; idx < messageLines.Count; idx++)
            {
                if (messageLines[idx].Trim().Contains("IF THIS WASN'T YOU"))
                {
                    code = messageLines[idx - 1].Trim();
                    break;
                }
            }
        }

        if (string.IsNullOrEmpty(code))
        {
            Console.WriteLine("Ok, this is a problem. Message {0} is a steamguard email and we have the " +
                              "account name {1}, but have no idea of the code to use.", message.Id, accountName);
            // TODO: Flag that there's an issue... somehow
            return null;
        }
        if (showDebuggingMessages)
        {
            Console.WriteLine("{0}: Account {1} needs steamguard code {2}", message.Id, accountName, code);
        }

        return new SteamGuardAccountCode
        {
            AccountName = accountName,
            Code = code
        };
    }

    public async Task<List<string>> GetAllMessageIds(GmailService service, string queryFilter)
    {
        try
        {
            var listRequest = service.Users.Messages.List("me");
            listRequest.IncludeSpamTrash = true; // In case it ends up in spam
            listRequest.Q = queryFilter;

            var response = await listRequest.ExecuteAsync();

            if (response == null)
            {
                Console.WriteLine("Failed to list messages. Try restarting the app to get a new token.");
                Environment.Exit(1);
            }

            if (response.Messages == null || response.Messages.Count == 0)
            {
                Console.WriteLine("The message request succeeded, but no messages were returned in the response. " +
                                  "If you expect there should be messages there, check the value of the query filter, " +
                                  "which is currently set to \"{0}\")", listRequest.Q);
                Environment.Exit(1);
            }

            var allMessageIds = response.Messages.Select(m => m.Id).ToList();

            while (!string.IsNullOrEmpty(response.NextPageToken))
            {
                listRequest.PageToken = response.NextPageToken;
                response = await listRequest.ExecuteAsync();
                allMessageIds.AddRange(response.Messages.Select(m => m.Id).ToList());
            }

            return allMessageIds;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception thrown: {0}. {1}", ex.Message, ex.InnerException?.Message);
            return new List<string>();
        }
    }

    public async Task<Message?> GetMessageById(GmailService service, string id)
    {
        try
        {
            var msgRequest = service.Users.Messages.Get("me", id);
            return await msgRequest.ExecuteAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception thrown: {0}. {1}", ex.Message, ex.InnerException?.Message);
            return null;
        }
    }

    public async Task<bool> SendDiscordWebhook(SteamGuardAccountCode sgCode, string webhookUrl)
    {
        return await SendDiscordWebhook($"New SteamGuard code. Account: **{sgCode.AccountName}**, code **{sgCode.Code}**", webhookUrl);
    }

    public async Task<bool> SendDiscordWebhook(string content, string webhookUrl)
    {
        try
        {
            await _client.PostAsJsonAsync(webhookUrl, new DiscordWebhookMessage { content = content });
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception thrown while trying send a discord webhook: {0}. {1}", ex.Message, ex.InnerException?.Message);
            return false;
        }

    }
}
