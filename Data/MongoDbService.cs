using MongoDB.Driver;

namespace dotnet_Learn.Data
{
    public class MongoDbService
    {
        private readonly IConfiguration _configuration;
        private readonly IMongoDatabase _database;
        public MongoDbService(IConfiguration configuration)
        {
            _configuration = configuration;
            var connectionString = _configuration.GetConnectionString("MongoDb");
            var databaseName = _configuration.GetConnectionString("DatabaseName");
            
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerApi = new ServerApi(ServerApiVersion.V1);
            var mongoClient = new MongoClient(settings); 
            _database = mongoClient.GetDatabase(databaseName);
        }
    
            public IMongoDatabase? database => _database;
    }
}
