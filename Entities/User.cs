using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace dotnet_Learn.Entities
{
    public class User
    {
        [BsonId]
        [BsonElement("_id"), BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("name"), BsonRepresentation(BsonType.String)]
        public string? Name { get; set; }
        [BsonElement("email"), BsonRepresentation(BsonType.String)]
        public string? Email { get; set; }
        [BsonElement("passwordHash"), BsonRepresentation(BsonType.String)]
        public string? PasswordHash { get; set; }

        [BsonElement("isOnline"), BsonRepresentation(BsonType.Boolean)]
        public bool IsOnline { get; set; } = false;

        [BsonElement("lastSeen"), BsonRepresentation(BsonType.DateTime)]
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    }
}
