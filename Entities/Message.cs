using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace dotnet_Learn.Entities
{
    public class Message
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string? Id { get; set; }

        [BsonElement("senderId"), BsonRepresentation(BsonType.String)]
        public string? SenderId { get; set; }

        [BsonElement("receiverId"), BsonRepresentation(BsonType.String)]
        public string? ReceiverId { get; set; }

        [BsonElement("text"), BsonRepresentation(BsonType.String)]
        public string? Text { get; set; }

        [BsonElement("timestamp"), BsonRepresentation(BsonType.DateTime)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
