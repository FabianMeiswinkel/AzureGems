using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Cosmos;

namespace AzureGems.CosmosDB
{
	public class CosmosDbClient : ICosmosDbClient, IDisposable
	{
		private readonly ContainerDefinition[] _containerDefinitions;

		private readonly CosmosClient _sdkClient;
		private readonly AsyncLazy<Database> _lazyDatabase;

		public IServiceProvider ServiceProvider { get; }

		private async Task<Database> GetDatabase()
		{
			return await _lazyDatabase.Value;
		}

		public ContainerDefinition GetContainerDefinition(string containerId)
		{
			ContainerDefinition containerDef = _containerDefinitions.FirstOrDefault(def => def.ContainerId == containerId);
			return containerDef;
		}

		public ContainerDefinition GetContainerDefinitionForType(Type t)
		{
			ContainerDefinition containerDefForT = _containerDefinitions.FirstOrDefault(def => def.EntityType == t);
			return containerDefForT;
		}

		public ContainerDefinition GetContainerDefinitionForType<T>()
		{
			Type t = typeof(T);
			return GetContainerDefinitionForType(t);
		}

		public CosmosDbClient(
			IServiceProvider serviceProvider,
			CosmosDbConnectionSettings connectionSettings,
			CosmosDbDatabaseConfig dbDatabaseConfig,
			IEnumerable<ContainerDefinition> containerDefinitions)
		{
			this.ServiceProvider = serviceProvider;

			IEnumerable<ContainerDefinition> definitions = containerDefinitions as ContainerDefinition[] ?? containerDefinitions.ToArray();
			_containerDefinitions = definitions.ToArray();

			_sdkClient = new CosmosClient(
				connectionSettings.EndPoint,
				connectionSettings.AuthKey,
				new CosmosClientOptions()
				{
					ConnectionMode = ConnectionMode.Direct,
					SerializerOptions = new CosmosSerializationOptions()
					{
						IgnoreNullValues = true,
						PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase
					}
				});

			_lazyDatabase = new AsyncLazy<Database>(async () =>
			{
				int retryCount = 0;
				while (true)
				{
					try
					{
						DatabaseResponse resp = await _sdkClient.CreateDatabaseIfNotExistsAsync(dbDatabaseConfig.DatabaseId, dbDatabaseConfig.SharedThroughput);

						foreach (ContainerDefinition containerDefinition in definitions)
						{
							await Internal_EnsureContainerExists(resp.Database, containerDefinition);
						}

						return resp.Database;
					}
					catch (Exception error)
					{
						if (retryCount > 10)
						{
							throw new SystemException("Cannot initialiye database", error);
						}

						Console.WriteLine("WARNING: Transient error initializing database - {0}", error);
						await Task.Delay(TimeSpan.FromSeconds(5));
					}
				}
			});
		}

		private static async Task<Container> Internal_EnsureContainerExists(Database db, ContainerDefinition containerDefinition)
		{
			return await Internal_EnsureContainerExists(db, containerDefinition.ContainerId, containerDefinition.PartitionKeyPath, containerDefinition.Throughput);
		}

		private static async Task<Container> Internal_EnsureContainerExists(Database db, string containerId, string partitionKeyPath, int? throughput)
		{
			var containerDefinition = new ContainerProperties(id: containerId, partitionKeyPath: partitionKeyPath);

			ContainerResponse response = await FakeCreateContainerIfNotExistsAsync(
				db: db,
				containerProperties: containerDefinition,
				throughput: throughput,
				requestOptions: null);

			return response.Container;
		}
		private async Task<Container> Internal_GetContainer(string containerId)
		{
			Database database = await GetDatabase();
			Container container = database.GetContainer(containerId);
			return container;
		}

		private readonly ConcurrentDictionary<string, ICosmosDbContainer> _containerCache = new ConcurrentDictionary<string, ICosmosDbContainer>();

		public async Task<ICosmosDbContainer> GetContainer(string containerId)
		{
			if (_containerCache.TryGetValue(containerId, out ICosmosDbContainer containerFromCache))
			{
				return containerFromCache;
			}

			Container container = await Internal_GetContainer(containerId);
			ContainerDefinition definition = GetContainerDefinition(containerId);
			ICosmosDbContainer newContainer = new CosmosDbContainer(definition, this, container);

			return _containerCache.GetOrAdd(containerId, newContainer);
		}

		public async Task<ICosmosDbContainer> GetContainer<TEntity>()
		{
			ContainerDefinition definition = GetContainerDefinitionForType<TEntity>();
			return await GetContainer(definition.ContainerId);
		}

		public void Dispose()
		{
			_sdkClient?.Dispose();
		}

        static long fakeCreateContainerIfNotExistsAsyncCallCount = 0;
        private static async Task<ContainerResponse> FakeCreateContainerIfNotExistsAsync(
            Database db,
            ContainerProperties containerProperties,
            int? throughput,
            RequestOptions requestOptions)
        {
            await Task.Yield();

            if (Interlocked.Increment(ref fakeCreateContainerIfNotExistsAsyncCallCount) <= 3)
            {
                String injectedActivityId = Guid.NewGuid().ToString();
                throw new CosmosException("Injected 404/1004", System.Net.HttpStatusCode.NotFound, 1004, injectedActivityId, 0);
            }

            return await db.CreateContainerIfNotExistsAsync(containerProperties, throughput, requestOptions);
        }
    }
}