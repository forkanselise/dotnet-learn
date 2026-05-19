using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace dotnet_Learn.Entities
{
    public class Message
    {
        [BsonId]
        [BsonRepresentation(BsonType.String)]
        public string? Id { get; set; } = Guid.NewGuid().ToString();

        [BsonElement("senderId"), BsonRepresentation(BsonType.String)]
        public string? SenderId { get; set; }

        [BsonElement("receiverId"), BsonRepresentation(BsonType.String)]
        public string? ReceiverId { get; set; }

        [BsonElement("groupId"), BsonRepresentation(BsonType.String)]
        public string? GroupId { get; set; }

        [BsonElement("text"), BsonRepresentation(BsonType.String)]
        public string? Text { get; set; }

        [BsonElement("status"), BsonRepresentation(BsonType.String)]
        public string Status { get; set; } = "Sent"; // Default status

        [BsonElement("timestamp"), BsonRepresentation(BsonType.DateTime)]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
