using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

// ─────────────────────────────────────────────────────────────────
// 트리밍(ILLink)이 COM 인터페이스의 메서드를 지웠는지 검사한다. CI(build.yml)가 self-contained publish 뒤에 돌린다.
//
// COM 인터페이스([ComImport] 또는 [InterfaceType])는 메서드 선언 순서가 곧 네이티브 vtable 슬롯 번호다. 트리머가 안 쓰는
// 메서드를 하나 지우면 그 뒤 슬롯이 전부 한 칸씩 당겨져, 런타임이 엉뚱한 함수를 부르다 "Fatal error. 0x80131506" 으로
// 프로세스가 죽는다. try/catch 로 못 잡고 errors.log 에도 남지 않는다. 비유하면 우편함 줄에서 빈 칸 하나를 빼 버려
// 그 뒤 우편물이 전부 옆집으로 가는 것이다. ("진단 정보 복사" 크래시: IEnumFORMATETC 의 Skip/Clone 이 잘렸었다)
//
// 사용법: ComTrimCheck <트리밍된 publish 폴더> <원본 폴더>...
//   publish 폴더는 단일 파일(PublishSingleFile)이 아닌 폴더 publish 결과다. 원본 폴더는 트리밍 전 어셈블리가 있는 곳:
//   런타임 팩(microsoft.netcore.app.runtime.win-<arch>, microsoft.windowsdesktop.app.runtime.win-<arch>)의 lib/net8.0 과
//   native(System.Private.CoreLib.dll), 그리고 앱의 빌드 출력(ImeBadge.dll, ImeBadge.Core.dll).
//   publish 폴더의 관리 어셈블리마다, 같은 이름의 파일이 있는 첫 번째 원본 폴더의 파일을 원본으로 삼는다.
//   원본을 못 찾은 어셈블리가 하나라도 있으면 검사가 빈 채로 통과하지 않도록 실패한다.
//
// 원본에서 COM 인터페이스를 찾아 트리밍 결과의 같은 형식과 메서드 개수를 비교한다. 형식이 통째로 빠진 것은 괜찮다
// (아무도 안 쓰니 그 vtable 을 부를 일도 없다). 개수가 줄었으면 실패.
//
// 종료 코드: 0 통과, 1 메서드가 잘린 COM 인터페이스가 있음, 2 사용법·입력 오류(폴더가 없음, 원본을 못 찾음 등).
// ─────────────────────────────────────────────────────────────────

// 줄어도 괜찮다고 확인한 인터페이스. "<어셈블리 파일 이름>: <형식 전체 이름>"
// - IToolboxService: Visual Studio 폼 디자이너의 도구 상자 서비스. 디자인 타임에만 쓰이고 실행 중인 앱은 부르지 않는다.
var allowed = new HashSet<string>(StringComparer.Ordinal)
{
    "System.Windows.Forms.Design.dll: System.Drawing.Design.IToolboxService",
};

bool github = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
void Error(string message) => Console.WriteLine((github ? "::error::" : "오류: ") + message);

if (args.Length < 2)
{
    Console.WriteLine("사용법: ComTrimCheck <트리밍된 publish 폴더> <원본 폴더>...");
    return 2;
}
foreach (string dir in args)
{
    if (!Directory.Exists(dir))
    {
        Error($"폴더가 없다: {dir}");
        return 2;
    }
}

string publishDir = args[0];
string[] originalDirs = args[1..];
int assemblies = 0, compared = 0, removedWhole = 0;
var shrunk = new List<string>();
var noOriginal = new List<string>();
var allowedSeen = new HashSet<string>(StringComparer.Ordinal);

foreach (string trimmedPath in Directory.GetFiles(publishDir, "*.dll").Order(StringComparer.OrdinalIgnoreCase))
{
    // 트리밍 결과 쪽은 특성(attribute)이 지워졌을 수도 있으니 인터페이스를 전부 모아 이름으로만 맞춘다.
    Dictionary<string, List<string>>? trimmed = ReadInterfaces(trimmedPath, comOnly: false);
    if (trimmed is null) continue;   // 네이티브 DLL(coreclr.dll 등)
    string file = Path.GetFileName(trimmedPath);
    string? originalPath = originalDirs.Select(d => Path.Combine(d, file)).FirstOrDefault(File.Exists);
    if (originalPath is null)
    {
        noOriginal.Add(file);
        continue;
    }
    Dictionary<string, List<string>> original = ReadInterfaces(originalPath, comOnly: true)
        ?? throw new InvalidDataException($"관리 어셈블리가 아니다: {originalPath}");
    assemblies++;

    foreach ((string type, List<string> methods) in original)
    {
        if (!trimmed.TryGetValue(type, out List<string>? kept))
        {
            removedWhole++;
            continue;
        }
        compared++;
        if (kept.Count >= methods.Count) continue;
        string key = $"{file}: {type}";
        string line = $"{key} — 메서드 {methods.Count} → {kept.Count}개 (잘림: {string.Join(", ", Subtract(methods, kept))})";
        if (allowed.Contains(key))
        {
            allowedSeen.Add(key);
            Console.WriteLine($"허용 목록(디자인 타임 전용): {line}");
        }
        else
        {
            shrunk.Add(line);
        }
    }
}

Console.WriteLine($"어셈블리 {assemblies}개에서 COM 인터페이스 {compared}개 비교 (트리밍으로 형식이 통째로 빠진 {removedWhole}개는 제외)");
foreach (string key in allowed.Where(k => !allowedSeen.Contains(k)))
    Console.WriteLine($"참고: 허용 목록의 {key} 는 이제 줄지 않는다. 목록에서 빼도 된다.");

foreach (string line in shrunk) Error(line);
if (shrunk.Count > 0)
{
    Error($"트리밍이 COM 인터페이스 {shrunk.Count}개의 메서드를 지웠다. vtable 슬롯이 당겨져 호출하는 순간 프로세스가 죽는다. "
        + "src/ImeBadge/ILLink.Descriptors.xml 에 그 형식(또는 네임스페이스)을 보존하도록 추가할 것.");
}
if (noOriginal.Count > 0)
    Error($"원본을 찾지 못한 어셈블리: {string.Join(", ", noOriginal)}. 원본 폴더 인자를 확인할 것.");
else if (compared == 0)
    Error("비교한 COM 인터페이스가 하나도 없다. publish 폴더와 원본 폴더 인자를 확인할 것.");

if (shrunk.Count > 0) return 1;
if (noOriginal.Count > 0 || compared == 0) return 2;
Console.WriteLine("통과: 트리밍으로 메서드가 줄어든 COM 인터페이스가 없다.");
return 0;

// 인터페이스 형식 전체 이름 → 선언 순서대로의 메서드 이름. 관리 어셈블리가 아니면 null.
// comOnly 면 COM 인터페이스(Import 플래그 = [ComImport], 또는 [InterfaceType] 특성)만 모은다.
static Dictionary<string, List<string>>? ReadInterfaces(string path, bool comOnly)
{
    using var pe = new PEReader(File.OpenRead(path));
    try
    {
        if (!pe.HasMetadata) return null;
    }
    catch (BadImageFormatException)
    {
        return null;
    }
    MetadataReader md = pe.GetMetadataReader();
    var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (TypeDefinitionHandle handle in md.TypeDefinitions)
    {
        TypeDefinition type = md.GetTypeDefinition(handle);
        if ((type.Attributes & TypeAttributes.Interface) == 0) continue;
        if (comOnly && (type.Attributes & TypeAttributes.Import) == 0 && !HasInterfaceTypeAttribute(md, type)) continue;
        result[FullName(md, type)] = type.GetMethods().Select(m => md.GetString(md.GetMethodDefinition(m).Name)).ToList();
    }
    return result;
}

static bool HasInterfaceTypeAttribute(MetadataReader md, TypeDefinition type)
{
    foreach (CustomAttributeHandle handle in type.GetCustomAttributes())
    {
        EntityHandle ctor = md.GetCustomAttribute(handle).Constructor;
        // 다른 어셈블리의 특성은 MemberReference, 같은 어셈블리(System.Private.CoreLib 안)의 특성은 MethodDefinition 이다.
        EntityHandle attributeType = ctor.Kind == HandleKind.MemberReference
            ? md.GetMemberReference((MemberReferenceHandle)ctor).Parent
            : md.GetMethodDefinition((MethodDefinitionHandle)ctor).GetDeclaringType();
        (StringHandle ns, StringHandle name) = attributeType.Kind switch
        {
            HandleKind.TypeReference => (md.GetTypeReference((TypeReferenceHandle)attributeType).Namespace,
                                         md.GetTypeReference((TypeReferenceHandle)attributeType).Name),
            HandleKind.TypeDefinition => (md.GetTypeDefinition((TypeDefinitionHandle)attributeType).Namespace,
                                          md.GetTypeDefinition((TypeDefinitionHandle)attributeType).Name),
            _ => (default, default),
        };
        if (!name.IsNil && md.StringComparer.Equals(name, "InterfaceTypeAttribute")
            && md.StringComparer.Equals(ns, "System.Runtime.InteropServices"))
            return true;
    }
    return false;
}

// 중첩 형식은 ILLink.Descriptors.xml 과 같은 "바깥형식/안쪽형식" 표기.
static string FullName(MetadataReader md, TypeDefinition type)
{
    string name = md.GetString(type.Name);
    TypeDefinitionHandle outer = type.GetDeclaringType();
    if (!outer.IsNil) return FullName(md, md.GetTypeDefinition(outer)) + "/" + name;
    string ns = md.GetString(type.Namespace);
    return ns.Length == 0 ? name : ns + "." + name;
}

// all 에서 kept 에 남은 것을 빼고(같은 이름 여러 개도 개수대로) 원래 순서대로 돌려준다.
static IEnumerable<string> Subtract(List<string> all, List<string> kept)
{
    var left = kept.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());
    foreach (string name in all)
    {
        if (left.TryGetValue(name, out int count) && count > 0) left[name] = count - 1;
        else yield return name;
    }
}
