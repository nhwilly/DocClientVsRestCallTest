using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Newtonsoft.Json;

namespace DocClientVsRestCallTest
{
    /// <summary>
    /// The purpose of this console app is to determine why I can't get a REST call
    /// to work on a read only resource token for Azure CosmosDb.  A direct comparison
    /// using identical paths and tokens should work.  I have an issue for sure, but I
    /// can't find it.  :(
    ///
    /// To run this, you need to have already created a partitioned CosmosDb collection.
    /// </summary>
    class Program
    {
        // ALL YOU NEED TO TEST THIS IS TO COMPLETE THE FOLLOWING FIVE VALUES.
        public static string cosmosAccountName = "[YOUR-COSMOS-ACCOUNT-NAME]";
        public static string databaseId = "YOUR-DATABASE-NAME";
        public static string collectionId = "YOUR-COLLECTION-NAME";
        public static string masterKey = $"[YOUR-MASTER-KEY-TO-SIMULATE-SERVER]";
        public static string partitionIdPath = "[YOUR-PARTITION-PATH]";


        // EVERYTHING FROM HERE DOWN CAN REMAIN UNMODIFIED.
        public static string dbServicePath = $"https://{cosmosAccountName}.documents.azure.com";
        public static string datetime = DateTime.UtcNow.ToString("R");
        public static string version = "2018-06-18";
        public static string resourceId = $"dbs/{databaseId}/colls/{collectionId}";
        public static string urlPath = $"{dbServicePath}/{resourceId}/docs";
        public static string partitionKey = $"TestPartition";
        public static string documentId = $"TestDocumentId";

        public static string queryString =
            $"select * from c where c.id='{documentId}' and c.{partitionIdPath} ='{partitionKey}'";

        public static string userId = $"TestUser";
        public static string permissionToken = string.Empty;

        // the master key is supplied to create permission tokens and simulate the server only.

        static void Main(string[] args)
        {
            Debug.WriteLine("Starting...");

            // This simulates what would happen on the server using a resource token 
            // broker.  This would not exist on the user's device.
            permissionToken =
                Task.Run(async () => await GetPermissionToken()).GetAwaiter().GetResult();

            QueryUsingSdk();

            QueryUsingRest();

            Task.Run(async () => await CleanUp()).ConfigureAwait(false);

            Console.ReadKey();
        }

        /// <summary>
        /// Queries using the REST API.
        /// </summary>
        /// <returns></returns>
        static void QueryUsingRest()
        {
            Uri uri = new Uri(urlPath);
            HttpClient client = new HttpClient() { };

            var encodedToken =
                HttpUtility.UrlEncode(permissionToken);

            string partitionAsJsonArray =
                JsonConvert.SerializeObject(new[] { partitionKey });

            client.DefaultRequestHeaders.Add("x-ms-date", datetime);
            client.DefaultRequestHeaders.Add("x-ms-documentdb-isquery", "True");
            client.DefaultRequestHeaders.Add("x-ms-documentdb-query-enablecrosspartition", "False");
            client.DefaultRequestHeaders.Add("x-ms-documentdb-query-iscontinuationexpected", "False");
            client.DefaultRequestHeaders.Add("x-ms-documentdb-partitionkey", partitionAsJsonArray);
            client.DefaultRequestHeaders.Add("authorization", encodedToken);
            client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
            client.DefaultRequestHeaders.Add("x-ms-version", version);
            client.DefaultRequestHeaders.Accept
                .Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var content =
                new StringContent(JsonConvert.SerializeObject(new { query = queryString }), Encoding.UTF8,
                    "application/query+json");

            HttpResponseMessage response =
                Task.Run(async () => await client.PostAsync(urlPath, content)).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"REST result: Fail-{response.StatusCode}!");
                Task.Run(async () => await
                    DisplayErrorMessage(response).ConfigureAwait(false)
                );
            }
            else
            {
                Console.WriteLine($"REST result: Success {response.StatusCode}!");

                var jsonString = Task.Run(async () => await
                   response.Content.ReadAsStringAsync().ConfigureAwait(false)
                );
            }
        }

        /// <summary>
        /// Queries using the DocumentClient from the SDK.
        /// </summary>
        /// <returns></returns>

        static void QueryUsingSdk()
        {

            var docClient =
                new DocumentClient(new Uri(dbServicePath), permissionToken);

            var feedOptions =
                new FeedOptions { PartitionKey = new PartitionKey(partitionKey) };

            var result =
                docClient
                    .CreateDocumentQuery(UriFactory.CreateDocumentCollectionUri(databaseId, collectionId), queryString,
                        feedOptions)
                    .ToList()
                    .First();

            var message = result?.message ?? "Response is null = fail.";
            Console.WriteLine($"SDK  result: {message}");
        }

        /// <summary>
        /// This method simulates what would happen on the server during an authenticated
        /// request.  The token (and other permission info) would be returned to the client.
        /// </summary>
        /// <returns></returns>
        static async Task<string> GetPermissionToken()
        {
            Console.WriteLine($"Getting resource token from server...");
            string token = string.Empty;
            try
            {
                var docClient =
                    new DocumentClient(new Uri(dbServicePath), masterKey);

                var userUri =
                    UriFactory.CreateUserUri(databaseId, userId);

                // delete the user if it exists...
                try
                {
                    await docClient.DeleteUserAsync(userUri).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"Delete user error: {e.Message}");
                }

                // create the user
                var dbUri =
                    UriFactory.CreateDatabaseUri(databaseId);

                await docClient.CreateUserAsync(dbUri, new User { Id = userId }).ConfigureAwait(false);

                // create the permission
                var link =
                    await docClient
                        .ReadDocumentCollectionAsync(UriFactory.CreateDocumentCollectionUri(databaseId, collectionId))
                        .ConfigureAwait(false);

                var resourceLink =
                    link.Resource.SelfLink;

                var permission =
                    new Permission
                    {
                        Id = partitionKey,
                        PermissionMode = PermissionMode.Read,
                        ResourceLink = resourceLink,
                        ResourcePartitionKey = new PartitionKey(partitionKey)
                    };

                await docClient.CreatePermissionAsync(userUri, permission).ConfigureAwait(false);

                // now create a document that should be returned when we do the query
                var doc = new { id = documentId, partitionId = partitionKey, message = "Test document that we created in the database" };
                try
                {
                    await docClient.DeleteDocumentAsync(UriFactory.CreateDocumentUri(databaseId, collectionId,
                            documentId), new RequestOptions { PartitionKey = new PartitionKey(partitionKey) })
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"Test document not found to delete - this is normal.");
                }

                try
                {
                    var document = await docClient
                        .CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(databaseId, collectionId), doc)
                        .ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    Debug.WriteLine($"Create document message: {e.Message}");
                }

                // now read the permission back as it would happen on the server
                var result = await docClient.ReadPermissionFeedAsync(userUri).ConfigureAwait(false);
                if (result.Count > 0)
                {
                    token = result.First(c => c.Id == partitionKey).Token;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Create and get permission failed: {ex.Message}");
            }

            if (string.IsNullOrEmpty(token))
            {
                Debug.WriteLine("Did not find token");
            }
            return token;
        }

        static async Task CleanUp()
        {
            Console.WriteLine($"Cleaning up...");
            var docClient =
                new DocumentClient(new Uri(dbServicePath), masterKey);

            try
            {
                await docClient.DeleteDocumentAsync(UriFactory.CreateDocumentUri(databaseId, collectionId,
                        documentId), new RequestOptions { PartitionKey = new PartitionKey(partitionKey) })
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Delete document message: {e.Message}");
            }
        }

        static async Task DisplayErrorMessage(HttpResponseMessage response)
        {
            var messageDefinition =
                new
                {
                    code = "",
                    message = ""
                };

            var jsonString =
                await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var message =
                JsonConvert.DeserializeAnonymousType(jsonString, messageDefinition);

            Debug.WriteLine($"Failed with {response.StatusCode} : {message.message}");
        }
    }
}