namespace Steamguard
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            // Create the file (if it doesn't exist) to track the messages we've read already
            var readIdsFilename = "read_ids.txt";
            if (!File.Exists(readIdsFilename))
            {
                File.Create(readIdsFilename);
            }

            var firstRun = !string.IsNullOrEmpty(args.FirstOrDefault(a =>
                a.Contains("firstrun=yes", StringComparison.InvariantCultureIgnoreCase)));

            var credentialFolderPath = args.FirstOrDefault(a => a.Contains("credentialFolder="));
            var clientSecretFile = args.FirstOrDefault(a => a.Contains("clientSecretFile="));
            var discordWebhookUrl = args.FirstOrDefault(a => a.Contains("discordWebhookUrl="));
            var loopSecondsArg = args.FirstOrDefault(a => a.Contains("loopSeconds="));

            if (string.IsNullOrEmpty(credentialFolderPath) || string.IsNullOrEmpty(clientSecretFile) || string.IsNullOrEmpty(discordWebhookUrl))
            {
                Console.WriteLine("One or more required arguments (credentialFolder, clientSecretFile, discordWebhookUrl) are missing.");
                Console.WriteLine("Usage example:");
                Console.WriteLine("  Steamguard.exe credentialFolder=c:\\temp\\steamguard clientSecretFile=client_secret.json discordWebhookUrl=https://discord.com/api/webhooks/somebigstringoftextlivesinhere");
                Console.WriteLine();
                Console.WriteLine("Optionally, append \"firstrun=yes\" to the argument list to sync the message ID list with the gmail account.");

                Environment.Exit(1);
            }

            // Loop seconds is an optional parameter 
            int loopSeconds = 30;
            if (!string.IsNullOrEmpty(loopSecondsArg))
            {
                loopSecondsArg = loopSecondsArg.Replace("loopSeconds=", "");

                if (int.TryParse(loopSecondsArg, out var parsedSeconds))
                {
                    loopSeconds = parsedSeconds;
                }
                else
                {
                    Console.WriteLine("The value passed with the loopSeconds argument {0} was not valid. Continuing with the default: {1}",
                        loopSecondsArg, loopSeconds);
                }
            }

            int loopMilliseconds = loopSeconds * 1000;

            discordWebhookUrl = discordWebhookUrl.Replace("discordWebhookUrl=", "");
            credentialFolderPath = credentialFolderPath.Replace("credentialFolder=", "");
            clientSecretFile = Path.Combine(credentialFolderPath, clientSecretFile.Replace("clientSecretFile=", ""));

            // Check that the folder and file exist
            if (!Directory.Exists(credentialFolderPath))
            {
                Console.WriteLine("The specified credential folder ({0}) does not exist.", credentialFolderPath);
                Environment.Exit(1);
            }

            if (!File.Exists(clientSecretFile))
            {
                Console.WriteLine("The specified client secret file ({0}) does not exist.", clientSecretFile);
                Environment.Exit(1);
            }


            var gmailService = GmailApiHelper.GetService(clientSecretFile, credentialFolderPath);

            var messageBodyContentFilter = "It looks like you are trying to log in from a new device".ToUpper();

            var actionHandler = new Actions();

            if (firstRun)
            {
                var allGmailMessageIds = await actionHandler.GetAllMessageIds(gmailService, "+Steam");

                await using (StreamWriter sw = new StreamWriter(readIdsFilename))
                {
                    foreach (var idToWrite in allGmailMessageIds)
                    {
                        await sw.WriteLineAsync(idToWrite);
                    }

                    await sw.FlushAsync();
                    sw.Close();
                }

                Console.WriteLine("First run complete. Executing this application without firstrun=true will now monitor the email address as per usual.");
                Environment.Exit(0);
            }

            Console.WriteLine("Waiting {0} seconds before running for the first time.", loopSeconds);
            await Task.Delay(loopMilliseconds);

            while (true)
            {
                // KNOWN ISSUE: There is currently no timeout set on the Gmail credential retrieval, and so if the refresh token expires, this will likely hang and do nothing.
                // So if the app appears to be doing nothing for more than a couple of minutes at a time, it probably just needs to be restarted so that it can pop up a
                // browser and ask for permission again.

                Console.WriteLine("Running...");
                // Refresh the gmail credentials
                gmailService = GmailApiHelper.GetService(clientSecretFile, credentialFolderPath);

                var alreadyReadIds = new List<string>();
                var line = "";

                using (StreamReader sr = new StreamReader(readIdsFilename))
                {
                    while ((line = sr.ReadLine()) != null)
                    {
                        alreadyReadIds.Add(line);
                    }

                    sr.Close();
                }

                // The queryFilter parameter accepts anything you can get valid results for in gmail, such as "from:someemailhere@gmail.com"
                var allGmailMessageIds = await actionHandler.GetAllMessageIds(gmailService, "+Steam");

                Console.WriteLine("Gmail returned {0} message IDs.", allGmailMessageIds.Count);

                var haventReadYet = allGmailMessageIds.Except(alreadyReadIds).ToList();
                var messageIdsProcessed = new List<string>();

                if (!haventReadYet.Any())
                {
                    Console.WriteLine("No codes to go through, we've read them all already");

                    Console.WriteLine("Loop run. Waiting for {0} seconds...", loopSeconds);

                    await Task.Delay(loopMilliseconds);
                    continue;
                }

                foreach (var messageId in haventReadYet)
                {
                    var message = await actionHandler.GetMessageById(gmailService, messageId);
                    if (message == null)
                    {
                        Console.WriteLine("Failed to get message with the ID {0}", message.Id);
                        continue;
                    }

                    var sgCode = actionHandler.GetSteamGuardCode(message, messageBodyContentFilter, false);
                    var messageIdProcessed = false;

                    if (sgCode != null)
                    {
                        Console.WriteLine("Message ID {0} has a valid steamguard code request: {1}, {2}", message.Id, sgCode.AccountName, sgCode.Code);
                        var webhookSent = await actionHandler.SendDiscordWebhook(sgCode, discordWebhookUrl);
                        // Don't add the message ID to the ones we've processed if it didn't send the webhook successfully... so the next time we check, we can try again
                        if (webhookSent)
                        {
                            messageIdProcessed = true;
                        }
                    }
                    else
                    {
                        // Log something if we want to
                        Console.WriteLine("[{0}] not a steamguard email", message.Id);
                        messageIdProcessed = true;
                    }

                    if (messageIdProcessed)
                    {
                        messageIdsProcessed.Add(messageId);
                    }
                }

                // Yes, if something goes wrong while we're reading messages, we don't update any of the ones we've already processed.
                // If that becomes a problem at any point, we can refactor this to keep the list up to date as each one is processed.
                // For now, this remains lazy unless I'm particularly inspired to refactor it

                var idsToWrite = new List<string>();
                idsToWrite.AddRange(alreadyReadIds);
                idsToWrite.AddRange(messageIdsProcessed);

                await using (StreamWriter sw = new StreamWriter(readIdsFilename))
                {
                    foreach (var idToWrite in idsToWrite)
                    {
                        await sw.WriteLineAsync(idToWrite);
                    }

                    await sw.FlushAsync();
                    sw.Close();
                }

                idsToWrite.Clear();

                Console.WriteLine("Loop run. Waiting for {0} seconds...", loopSeconds);

                await Task.Delay(loopMilliseconds);
            }
        }
    }
}
