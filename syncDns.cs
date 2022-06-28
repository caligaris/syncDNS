using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Azure.Identity;
using System.Net.Http;
using Azure.Core;
using System.Text.Json.Nodes;
using System.Text;
using System.Text.Json;
using System.Linq;

namespace syncDns
{
    public class syncDns
    {
        private readonly ILogger<syncDns> _logger;
        private readonly string _dnsZoneResourceId;
        private readonly string _aRecordName;
        public syncDns(ILogger<syncDns> logger)
        {
            _logger = logger;
            _dnsZoneResourceId = Environment.GetEnvironmentVariable("DNS_ZONE_RESOURCEID");
            _aRecordName = Environment.GetEnvironmentVariable("A_RECORD_NAME");
        }
        [FunctionName("syncDns")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var data = JsonSerializer.Deserialize<JsonObject>(requestBody);
            var resourceId = data["data"]["context"]["activityLog"]["resourceId"].GetValue<string>();

            if (string.IsNullOrEmpty(resourceId))
            {
                return new BadRequestObjectResult("Empty resourceId in Microsoft.Insights/activityLogs message");
            }

            var credentials = new DefaultAzureCredential();
            var token = await credentials.GetTokenAsync(new TokenRequestContext(scopes: new string[] { "https://management.azure.com/.default" }));

            var url = $"https://management.azure.com{resourceId}/virtualMachines?api-version=2022-03-01";
            string updateResult;

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                //log.LogInformation($"Token: {token.Token}");
                request.Headers.Add("Authorization", $"Bearer {token.Token}");
                request.Headers.Add("Accept", "application/json");

                using (var response = await new HttpClient().SendAsync(request))
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    log.LogInformation(responseBody);

                    var document = JsonNode.Parse(responseBody);
                    JsonNode root = document.Root;
                    JsonArray instances = root["value"]!.AsArray();

                    var workerNodes = await Task.WhenAll(
                        instances.Select(instance => GetWorkerNode(instance, token.Token)));
                    updateResult = await UpdateDNSZone(workerNodes, token.Token); 

                }
            }
            return new OkObjectResult($"{updateResult}");
        }
        private async Task<workerNode> GetWorkerNode(JsonNode instance, string token)
        {
            var instanceId = instance["instanceId"].GetValue<string>();
            var resourceId = instance["id"].GetValue<string>();
            var networkUrl = $"https://management.azure.com{resourceId}/networkInterfaces?api-version=2021-03-01";
            _logger.LogInformation($"Get node IP for Instance: {instanceId}");

            var nodeIP = await GetNodeIP(networkUrl, token);
            return new workerNode()
            {
                id = instanceId,
                ipv4Address = nodeIP
            };
        }

        private async Task<string> GetNodeIP(string url, string token)
        {
            string nodeIP;
            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            {
                request.Headers.Add("Authorization", $"Bearer {token}");
                request.Headers.Add("Accept", "application/json");

                using (var response = await new HttpClient().SendAsync(request))
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation(responseBody);

                    var document = JsonNode.Parse(responseBody);
                    JsonNode root = document.Root;
                    nodeIP = root["value"][0]["properties"]["ipConfigurations"][0]["properties"]["privateIPAddress"].GetValue<string>();
                }
            }
            return nodeIP;
        }
        private async Task<string> UpdateDNSZone(workerNode[] workerNodes, string token){
            var updateRecordUrl = $"https://management.azure.com{_dnsZoneResourceId}/A/{_aRecordName}?api-version=2018-09-01";
            string result;

            using (var request = new HttpRequestMessage(HttpMethod.Put, updateRecordUrl)){
                request.Headers.Add("Authorization", $"Bearer {token}");
                request.Headers.Add("Accept", "application/json");

                var body = new JsonObject();               
                body["properties"] = new JsonObject();
                body["properties"]["ttl"] = 3600;
                var aRecords = new JsonArray();
                foreach (var node in workerNodes)
                {
                    var record = new JsonObject();
                    record["ipv4Address"] = node.ipv4Address;
                    aRecords.Add(record);
                }
                body["properties"]["aRecords"] = aRecords;

                //serialize JSON to a string and set it as the body of the request
                request.Content = new StringContent(body.ToString(), Encoding.UTF8, "application/json");
                
                _logger.LogInformation($"Updating DNS Zone");
                using (var response = await new HttpClient().SendAsync(request)){
                    var responseBody = await response.Content.ReadAsStringAsync();
                    response.EnsureSuccessStatusCode();
                    _logger.LogInformation($"DNS Zone Updated:\n{responseBody}");
                    result = responseBody;
                }
            }
            return result;
        }
    }

    class workerNode
    {
        public string id { get; set; }
        public string ipv4Address { get; set; }
    }
}
