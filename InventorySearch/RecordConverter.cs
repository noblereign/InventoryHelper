using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using static InventoryHelper.CoreSearch;

namespace InventoryHelper
{
    public class RecordConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(SerializableRecord).IsAssignableFrom(objectType);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            JObject item = JObject.Load(reader);
            var recordType = item["RecordType"];
            SerializableRecord targetObject;

            if (recordType != null && (recordType.ToString() == "directory" || recordType.ToString() == "link"))
            {
                targetObject = new SerializableRecordDirectory();
            }
            else
            {
                targetObject = new SerializableRecord();
            }
            serializer.Populate(item.CreateReader(), targetObject);

            return targetObject;
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
        public override bool CanWrite => false;
    }
}