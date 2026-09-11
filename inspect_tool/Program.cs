using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;

string dllPath = @"C:\Users\brett\.nuget\packages\plugin.localnotification\12.0.1\lib\net8.0-android34.0\Plugin.LocalNotification.dll";
using var fs = File.OpenRead(dllPath);
using var pe = new PEReader(fs);
var md = pe.GetMetadataReader();

foreach (var typeHandle in md.TypeDefinitions)
{
    var td = md.GetTypeDefinition(typeHandle);
    var ns = md.GetString(td.Namespace);
    var name = md.GetString(td.Name);
    foreach (var methodHandle in td.GetMethods())
    {
        var m = md.GetMethodDefinition(methodHandle);
        var mname = md.GetString(m.Name);
        if (mname == "CreateNotificationChannels")
        {
            var sig = m.DecodeSignature(new SigProvider(), null);
            Console.WriteLine($"{ns}.{name}::{mname} params: {string.Join(", ", sig.ParameterTypes)}");
        }
    }
}

class SigProvider : ISignatureTypeProvider<string, object?>
{
    public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
    public string GetByReferenceType(string elementType) => elementType + "&";
    public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType + "<" + string.Join(",", typeArguments) + ">";
    public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
    public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
    public string GetPinnedType(string elementType) => elementType;
    public string GetPointerType(string elementType) => elementType + "*";
    public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
    public string GetSZArrayType(string elementType) => elementType + "[]";
    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeDefinition(handle).Name);
    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => reader.GetString(reader.GetTypeReference(handle).Name);
    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "typespec";
}
