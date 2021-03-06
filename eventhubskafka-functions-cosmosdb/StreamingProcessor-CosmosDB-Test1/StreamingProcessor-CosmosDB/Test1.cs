using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Extensions.Kafka;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;

namespace StreamingProcessor
{
    public static class Test1
    {
        public static string wwwRootFolder;
        /*
         * Cosmos DB output without using binding
         */
        [FunctionName("Test1")]
        public static async Task RunAsync(
            ExecutionContext ctx, 
            [KafkaTrigger("%KafkaBrokers%", "%EventHubName%",
            ConsumerGroup = "%ConsumerGroup%",
            EventHubConnectionString = "EventHubsConnectionString", 
            Protocol = BrokerProtocol.SaslSsl, 
            AuthenticationMode = BrokerAuthenticationMode.Plain, 
            SslCaLocation =  "D:\\home\\site\\wwwroot\\cacert.pem", // TODO: come with a better way to specify the path in KafkaTrigger
            Username = "$ConnectionString", 
            Password = "EventHubsConnectionString")] KafkaEventData<string>[] kafkaEvents, 
            [CosmosDB(databaseName: "%CosmosDBDatabaseName%", collectionName: "%CosmosDBCollectionName%", ConnectionStringSetting = "CosmosDBConnectionString")] IAsyncCollector<JObject> cosmosMessage,
            ILogger log)
        {   
            var client = await CosmosDBClient.GetClient();

            var tasks = new List<Task<ResourceResponse<Document>>>();
            long len = 0;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            double totalRUbyBatch = 0;
            int positionInBatch = 1;
            foreach (var data in kafkaEvents)
            {
                try
                {
                    string message = data.Value;
                    len += message.Length;

                    var document = JObject.Parse(message);
                    document["id"] = document["eventId"];
                    document["enqueuedAt"] = data.Timestamp;
                    document["processedAt"] = DateTime.UtcNow;
                    document["positionInBatch"] = positionInBatch;

                    tasks.Add(client.CreateDocumentAsync(document));

                    positionInBatch += 1;
                }
                catch (Exception ex)
                {
                    log.LogError($"{ex} - {ex.Message}");
                }
            }

            await Task.WhenAll(tasks);

            foreach (var t in tasks)
            {
                totalRUbyBatch += t.GetAwaiter().GetResult().RequestCharge;
            }

            sw.Stop();

            string logMessage = $"[Test1] T:{len} doc - E:{sw.ElapsedMilliseconds} msec";
            if (len > 0)
            {
                logMessage += Environment.NewLine + $"AVG:{(sw.ElapsedMilliseconds / len):N3} msec";
                logMessage += Environment.NewLine + $"RU:{totalRUbyBatch}. AVG RU:{(totalRUbyBatch / len):N3}";                
            }

            log.LogInformation(logMessage);
        }
    }

    
}
