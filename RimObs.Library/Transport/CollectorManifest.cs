using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Xml;

namespace RimWorks.RimObs.Transport;

[DataContract]
public sealed class CollectorManifest {
    [DataMember(Name = "schema_version")]
    public int SchemaVersion { get; set; }

    [DataMember(Name = "version")]
    public string? Version { get; set; }

    [DataMember(Name = "library_schema_compat")]
    public CollectorSchemaCompat? LibrarySchemaCompat { get; set; }

    public static CollectorManifest? TryParse(string json) {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            using (MemoryStream stream = new MemoryStream(bytes)) {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(CollectorManifest));
                return serializer.ReadObject(stream) as CollectorManifest;
            }
        }
        catch (SerializationException) {
            return null;
        }
        catch (XmlException) {
            return null;
        }
    }

    public static CollectorManifest? TryReadFile(string path) {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        try {
            if (!File.Exists(path))
                return null;
            return TryParse(File.ReadAllText(path));
        }
        catch (IOException) {
            return null;
        }
        catch (UnauthorizedAccessException) {
            return null;
        }
    }
}
