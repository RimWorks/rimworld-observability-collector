using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace RimWorks.RimObs.Config;

[DataContract]
internal sealed class CollectorConfigDocument {
    [DataMember(Name = "schema_version")]
    public int SchemaVersion { get; set; }

    [DataMember(Name = "sections")]
    public CollectorSectionsConfig? Sections { get; set; }

    public static CollectorConfigDocument? TryParse(string json) {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream stream = new MemoryStream(bytes)) {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CollectorConfigDocument));
                return serializer.ReadObject(stream) as CollectorConfigDocument;
            }
        }
        catch (SerializationException) {
            return null;
        }
        catch (XmlException) {
            return null;
        }
    }
}
