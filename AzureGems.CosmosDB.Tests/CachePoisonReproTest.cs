using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace AzureGems.CosmosDB.Tests
{
    [TestClass]
    public sealed class CachePoisonReproTest
    {
        [TestMethod]
        public void CachePoisonRepro()
        {
            CosmosDbContainerFactory containerFactory = new CosmosDbContainerFactory();

            String key = Environment.GetEnvironmentVariable("COSMOS_KEY");
            String endpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT");
            ContainerDefinition containerDef = new ContainerDefinition("DummyContainer", "/id", typeof(JObject));
            CosmosDbConnectionSettings dummyConnectionSettings = 
                new CosmosDbConnectionSettings(endpoint, key);
            CosmosDbDatabaseConfig dummyDbConfig = new CosmosDbDatabaseConfig("dummyDb", sharedThroughput: null);

            ICosmosDbClient dummyClient = new CosmosDbClient(
                serviceProvider: null,
                connectionSettings: dummyConnectionSettings,
                dbDatabaseConfig: dummyDbConfig,
                containerDefinitions: [containerDef]);

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    var c = containerFactory.Create(
                        typeof(CachePoisonReproTest),
                        containerDef,
                        dummyClient);

                    Console.WriteLine("{0} - SUCCESS {1}", i, c);
                }
                catch (Exception error)
                {
                    Console.WriteLine("{0} - EXCEPTION: {1}", i, error);
                }
            }
        }
    }
}
