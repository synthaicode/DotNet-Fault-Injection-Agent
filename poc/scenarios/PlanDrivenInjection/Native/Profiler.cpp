#include <atomic>
#include <codecvt>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <locale>
#include <set>
#include <map>
#include <regex>
#include <algorithm>
#include <cctype>
#include <openssl/sha.h>
#include <nlohmann/json.hpp>

#define __declspec(x)
// The upstream compatibility headers declare a convenience template using
// __uuidof. This extension always queries interfaces with explicit IIDs.
#define __uuidof(x) IID_IUnknown
#include "no_sal.h"
#include "mincom/mincom.h"
#define __RPC_H__
#define __PALRT_H__
typedef void* PRPC_MESSAGE;
typedef void* RPC_IF_HANDLE;
#include <cor.h>
#include <corhdr.h>
#include <clrprofiler.h>
#include "InstrumentationEngine.h"

namespace
{
constexpr CLSID Extension = {0x9f919afe, 0xc6f4, 0x4263, {0xa1, 0x12, 0x8d, 0xec, 0x99, 0x47, 0xbc, 0x1e}};
constexpr IID Unknown = {0, 0, 0, {0xc0, 0, 0, 0, 0, 0, 0, 0x46}};
constexpr IID FactoryId = {1, 0, 0, {0xc0, 0, 0, 0, 0, 0, 0, 0x46}};
constexpr IID MetadataImportId = {0x7dac8207, 0xd3ae, 0x4c75, {0x9b, 0x67, 0x92, 0x80, 0x1a, 0x49, 0x7d, 0x44}};

template<class T> struct Ptr
{
    T* value = nullptr;
    ~Ptr() { if (value) value->Release(); }
    T* operator->() const { return value; }
    T** out() { return &value; }
    void** asVoid() { return reinterpret_cast<void**>(&value); }
};

bool Same(REFIID left, REFIID right)
{
    return std::memcmp(&left, &right, sizeof(IID)) == 0;
}

template<class Interface, const IID& Id> class ComObject : public Interface
{
public:
    HRESULT QueryInterface(REFIID iid, void** object) override
    {
        *object = nullptr;
        if (!Same(iid, Unknown) && !Same(iid, Id)) return E_NOINTERFACE;
        *object = static_cast<Interface*>(this);
        AddRef();
        return S_OK;
    }
    ULONG AddRef() override { return ++references; }
    ULONG Release() override
    {
        const auto left = --references;
        if (!left) delete this;
        return left;
    }

protected:
    virtual ~ComObject() = default;

private:
    std::atomic<ULONG> references{1};
};

std::string GuidText(const GUID& id)
{
    char text[37];
    std::snprintf(text, sizeof(text), "%08x-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
        id.Data1, id.Data2, id.Data3, id.Data4[0], id.Data4[1], id.Data4[2], id.Data4[3],
        id.Data4[4], id.Data4[5], id.Data4[6], id.Data4[7]);
    return text;
}

std::string HashBytes(const std::vector<unsigned char>& bytes)
{
    unsigned char digest[SHA256_DIGEST_LENGTH];
    SHA256(bytes.data(), bytes.size(), digest);
    std::string result;
    for (const auto byte : digest)
    {
        result += "0123456789ABCDEF"[byte >> 4];
        result += "0123456789ABCDEF"[byte & 15];
    }
    return result;
}

std::string Hash(const std::string& file)
{
    std::ifstream input(file, std::ios::binary | std::ios::ate);
    const auto size = input.tellg();
    if (size <= 0 || size > 16 * 1024 * 1024) throw std::runtime_error("invalid target size");
    std::vector<unsigned char> bytes(static_cast<size_t>(size));
    input.seekg(0);
    if (!input.read(reinterpret_cast<char*>(bytes.data()), bytes.size()))
        throw std::runtime_error("cannot read target");
    return HashBytes(bytes);
}

class Method final : public ComObject<IInstrumentationMethod, IID_IInstrumentationMethod>
{
public:
    HRESULT Initialize(IProfilerManager* manager) override
    {
        const auto result = manager->QueryInterface(IID_IProfilerStringManager, strings.asVoid());
        if (FAILED(result)) return result;
        try { LoadPlan(); }
        catch (const std::exception& error)
        {
            std::fprintf(stderr, "plan rejected: %s\n", error.what());
        }
        return S_OK;
    }

    HRESULT ShouldInstrumentMethod(IMethodInfo* method, BOOL rejit, BOOL* instrument) override
    {
        *instrument = FALSE;
        if (!ready || rejit) return S_OK;
        mdMethodDef candidate;
        HRESULT result = method->GetMethodToken(&candidate);
        if (FAILED(result) || candidate != token) return result;
        Ptr<IModuleInfo> module;
        result = method->GetModuleInfo(module.out());
        if (FAILED(result)) return result;
        BSTR path = nullptr;
        result = module->GetFullPath(&path);
        if (FAILED(result)) return result;
        const bool matches = path && modulePath == path;
        strings->FreeString(path);
        if (!matches) return S_OK;

        GUID id;
        result = module->GetMVID(&id);
        if (FAILED(result)) return result;
        *instrument = GuidText(id) == mvid;
        if (!*instrument) std::fprintf(stderr, "plan rejected: loaded module MVID mismatch\n");
        return S_OK;
    }

    HRESULT InstrumentMethod(IMethodInfo* method, BOOL) override
    {
        Ptr<IInstructionGraph> graph;
        HRESULT result = method->GetInstructions(graph.out());
        if (FAILED(result)) return result;
        Ptr<IInstruction> call;
        result = graph->GetInstructionAtOriginalOffset(offset, call.out());
        if (FAILED(result) || !call.value) return E_FAIL;
        ILOrdinalOpcode opcode;
        Ptr<IOperandInstruction> operand;
        mdMemberRef original;
        if (FAILED(call->GetOpCode(&opcode)) || opcode != expectedOpcode ||
            FAILED(call->QueryInterface(IID_IOperandInstruction, operand.asVoid())) ||
            FAILED(operand->GetOperandValue(sizeof(original), reinterpret_cast<BYTE*>(&original))) ||
            original != expectedMember)
        {
            std::fprintf(stderr, "plan rejected at call site: opcode or member token mismatch\n");
            return E_FAIL;
        }

        Ptr<IModuleInfo> module;
        Ptr<IUnknown> import, emit;
        Ptr<IMetaDataImport> metadata;
        Ptr<IMetaDataEmit> editor;
        constexpr IID emitId = {0xba3fee4c, 0xecb9, 0x4e41, {0x83,0xb7,0x18,0x3f,0xa4,0x1c,0xd8,0x59}};
        if (FAILED(method->GetModuleInfo(module.out())) ||
            FAILED(module->GetMetaDataImport(import.out())) ||
            FAILED(import->QueryInterface(MetadataImportId, metadata.asVoid())) ||
            FAILED(module->GetMetaDataEmit(emit.out())) ||
            FAILED(emit->QueryInterface(emitId, editor.asVoid())))
            return E_FAIL;

        WCHAR actualName[1025]{};
        if (FAILED(metadata->GetMemberRefProps(original, nullptr, actualName, 1025, nullptr, nullptr, nullptr)) ||
            std::u16string(actualName) != memberName)
        {
            std::fprintf(stderr, "plan rejected at call site: member name mismatch\n");
            return E_FAIL;
        }

        Ptr<IMetaDataAssemblyEmit> assemblyEditor;
        constexpr IID assemblyEmitId = {0x211ef15b, 0x5317, 0x4438, {0xb1,0x96,0xde,0xc8,0x7b,0x88,0x76,0x93}};
        if (FAILED(emit->QueryInterface(assemblyEmitId, assemblyEditor.asVoid()))) return E_FAIL;
        ASSEMBLYMETADATA identity{};
        identity.usMajorVersion = 1;
        mdAssemblyRef runtimeAssembly;
        if (FAILED(assemblyEditor->DefineAssemblyRef(nullptr, 0, u"DotNetFaultInjectionAgent.Runtime",
                &identity, nullptr, 0, 0, &runtimeAssembly))) return E_FAIL;
        mdTypeRef runtimeType;
        if (FAILED(editor->DefineTypeRefByName(runtimeAssembly,
                u"DotNetFaultInjectionAgent.Runtime.FaultRuntime", &runtimeType))) return E_FAIL;
        mdMemberRef beforeCall;
        const BYTE signature[] = {0, 1, 1, 0x0e}; // static void(string)
        if (FAILED(editor->DefineMemberRef(runtimeType, u"BeforeCall", signature,
                sizeof(signature), &beforeCall))) return E_FAIL;
        const auto text = std::wstring_convert<std::codecvt_utf8_utf16<char16_t>, char16_t>().from_bytes(planHash);
        mdString hashText;
        if (FAILED(editor->DefineUserString(text.c_str(), text.size(), &hashText))) return E_FAIL;
        Ptr<IInstructionFactory> factory;
        Ptr<IInstruction> loadHash, invoke;
        if (FAILED(method->GetInstructionFactory(factory.out())) ||
            FAILED(factory->CreateTokenOperandInstruction(Cee_Ldstr, hashText, loadHash.out())) ||
            FAILED(factory->CreateTokenOperandInstruction(Cee_Call, beforeCall, invoke.out())) ||
            FAILED(graph->InsertBefore(call.value, loadHash.value)) ||
            FAILED(graph->InsertBefore(call.value, invoke.value))) return E_FAIL;
        std::fprintf(stderr, "IL rewritten: methodToken=%u offset=%u\n", static_cast<unsigned>(token), static_cast<unsigned>(offset));
        return S_OK;
    }

    HRESULT OnAppDomainCreated(IAppDomainInfo*) override { return S_OK; }
    HRESULT OnAppDomainShutdown(IAppDomainInfo*) override { return S_OK; }
    HRESULT OnAssemblyLoaded(IAssemblyInfo*) override { return S_OK; }
    HRESULT OnAssemblyUnloaded(IAssemblyInfo*) override { return S_OK; }
    HRESULT OnModuleLoaded(IModuleInfo*) override { return S_OK; }
    HRESULT OnModuleUnloaded(IModuleInfo*) override { return S_OK; }
    HRESULT OnShutdown() override { return S_OK; }
    HRESULT BeforeInstrumentMethod(IMethodInfo*, BOOL) override { return S_OK; }
    HRESULT OnInstrumentationComplete(IMethodInfo*, BOOL) override { return S_OK; }
    HRESULT AllowInlineSite(IMethodInfo*, IMethodInfo* callee, BOOL* allow) override
    {
        BOOL selected = FALSE;
        *allow = TRUE;
        if (ready && SUCCEEDED(ShouldInstrumentMethod(callee, FALSE, &selected)) && selected)
            *allow = FALSE;
        return S_OK;
    }

private:
    static void Keys(const nlohmann::json& value, std::initializer_list<const char*> keys)
    {
        if (!value.is_object() || value.size() != keys.size()) throw std::runtime_error("unexpected plan keys");
        for (const auto* key : keys) if (!value.contains(key)) throw std::runtime_error("missing plan key");
    }

    void LoadPlan()
    {
        const char* path = std::getenv("DOTNET_FAULT_INJECTION_PLAN");
        if (!path) throw std::runtime_error("DOTNET_FAULT_INJECTION_PLAN is missing");
        std::ifstream input(path, std::ios::binary | std::ios::ate);
        const auto size = input.tellg();
        if (size <= 0 || size > 65536) throw std::runtime_error("invalid plan size");
        input.seekg(0);
        std::vector<unsigned char> bytes(static_cast<size_t>(size));
        if (!input.read(reinterpret_cast<char*>(bytes.data()), bytes.size()))
            throw std::runtime_error("cannot read plan");
        std::map<int, std::set<std::string>> keys;
        bool duplicate = false;
        const auto plan = nlohmann::json::parse(bytes, [&](int depth, auto event, auto& value)
        {
            if (depth > 16) throw std::runtime_error("plan nesting limit exceeded");
            if (event == nlohmann::json::parse_event_t::object_start) keys[depth + 1].clear();
            if (event == nlohmann::json::parse_event_t::key)
                duplicate |= !keys[depth].insert(value.template get<std::string>()).second;
            return true;
        });
        if (duplicate) throw std::runtime_error("duplicate JSON key");
        Keys(plan, {"version","ruleId","target","site","conditions","fault","maxFirings"});
        Keys(plan.at("target"), {"path","sha256","mvid"});
        Keys(plan.at("site"), {"methodToken","ilOffset","memberToken","opcode","memberName"});
        Keys(plan.at("conditions"), {"environment","startAtInvocation"});
        Keys(plan.at("fault"), {"kind","exceptionType","message","delayMs"});
        if (!plan.at("version").is_number_integer() || plan.at("version") != 3)
            throw std::runtime_error("unsupported plan version");
        const auto ruleId = plan.at("ruleId").get<std::string>();
        if (!std::regex_match(ruleId, std::regex("[A-Za-z0-9_.-]{1,128}")))
            throw std::runtime_error("invalid ruleId");
        const auto& conditions = plan.at("conditions");
        const auto& environment = conditions.at("environment");
        if (!environment.is_object() || environment.size() > 16 ||
            !conditions.at("startAtInvocation").is_number_integer() ||
            conditions.at("startAtInvocation").get<int64_t>() < 1 ||
            conditions.at("startAtInvocation").get<int64_t>() > 1000000)
            throw std::runtime_error("invalid conditions");
        for (const auto& item : environment.items())
        {
            if (!std::regex_match(item.key(), std::regex("[A-Za-z_][A-Za-z0-9_]{0,127}")) ||
                !item.value().is_string() || item.value().get<std::string>().size() > 256 ||
                item.value().get<std::string>().find('\0') != std::string::npos)
                throw std::runtime_error("invalid environment condition");
        }
        if (!plan.at("maxFirings").is_number_integer() ||
            plan.at("maxFirings").get<int64_t>() < 0 || plan.at("maxFirings").get<int64_t>() > 100)
            throw std::runtime_error("invalid firing budget");
        const auto& fault = plan.at("fault");
        const auto kind = fault.at("kind").get<std::string>();
        const auto exception = fault.at("exceptionType").get<std::string>();
        const auto message = fault.at("message").get<std::string>();
        if (!fault.at("delayMs").is_number_integer()) throw std::runtime_error("invalid delay");
        const auto delay = fault.at("delayMs").get<int64_t>();
        if (kind == "throw")
        {
            if ((exception != "System.InvalidOperationException" && exception != "System.TimeoutException" &&
                 exception != "System.IO.IOException") || delay != 0 || message.size() > 1024)
                throw std::runtime_error("unsupported throw fault");
        }
        else if (kind == "delay")
        {
            if (delay < 1 || delay > 10000 || !exception.empty() || !message.empty())
                throw std::runtime_error("invalid delay fault");
        }
        else throw std::runtime_error("unsupported fault kind");
        const auto targetPath = plan.at("target").at("path").get<std::string>();
        if (targetPath.size() > 4096 || targetPath.find('\0') != std::string::npos)
            throw std::runtime_error("invalid target path");
        if (!std::filesystem::path(targetPath).is_absolute()) throw std::runtime_error("target path must be absolute");
        const auto target = std::filesystem::canonical(targetPath);
        auto expectedHash = plan.at("target").at("sha256").get<std::string>();
        std::transform(expectedHash.begin(), expectedHash.end(), expectedHash.begin(),
            [](unsigned char c) { return std::toupper(c); });
        if (expectedHash != Hash(target.string())) throw std::runtime_error("target SHA-256 mismatch");
        mvid = plan.at("target").at("mvid").get<std::string>();
        std::transform(mvid.begin(), mvid.end(), mvid.begin(),
            [](unsigned char c) { return std::tolower(c); });
        if (!std::regex_match(mvid, std::regex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")))
            throw std::runtime_error("invalid MVID");
        const auto& site = plan.at("site");
        if (!site.at("methodToken").is_number_integer() || !site.at("memberToken").is_number_integer() ||
            !site.at("ilOffset").is_number_integer()) throw std::runtime_error("invalid site types");
        const auto method = site.at("methodToken").get<int64_t>();
        const auto member = site.at("memberToken").get<int64_t>();
        const auto position = site.at("ilOffset").get<int64_t>();
        if (method < 0x06000001 || method > 0x06ffffff || member < 0x0a000001 ||
            member > 0x0affffff || position < 0 || position > UINT32_MAX)
            throw std::runtime_error("invalid site identity");
        const auto opcode = site.at("opcode").get<std::string>();
        if (opcode != "call" && opcode != "callvirt") throw std::runtime_error("unsupported opcode");
        const auto name = site.at("memberName").get<std::string>();
        if (name.empty() || name.size() > 1024) throw std::runtime_error("invalid member name");
        memberName = std::wstring_convert<std::codecvt_utf8_utf16<char16_t>, char16_t>().from_bytes(name);
        token = static_cast<mdMethodDef>(method);
        offset = static_cast<DWORD>(position);
        expectedMember = static_cast<mdMemberRef>(member);
        expectedOpcode = opcode == "call" ? Cee_Call : Cee_Callvirt;
        modulePath = std::wstring_convert<std::codecvt_utf8_utf16<char16_t>, char16_t>().from_bytes(target.string());
        planHash = HashBytes(bytes);
        ready = true;
        std::fprintf(stderr, "plan accepted: rule=%s target=%s\n", ruleId.c_str(), target.string().c_str());
    }

    Ptr<IProfilerStringManager> strings;
    std::u16string modulePath;
    std::string mvid;
    mdMethodDef token{};
    mdMemberRef expectedMember{};
    DWORD offset{};
    ILOrdinalOpcode expectedOpcode{};
    std::u16string memberName;
    std::string planHash;
    bool ready = false;
};

class Factory final : public ComObject<IClassFactory, FactoryId>
{
public:
    HRESULT CreateInstance(IUnknown* outer, REFIID iid, void** object) override
    {
        *object = nullptr;
        if (outer) return E_INVALIDARG;
        auto* method = new (std::nothrow) Method();
        if (!method) return E_OUTOFMEMORY;
        const auto result = method->QueryInterface(iid, object);
        method->Release();
        return result;
    }
    HRESULT LockServer(BOOL) override { return E_NOTIMPL; }
};
}

extern "C" __attribute__((visibility("default"))) HRESULT DllGetClassObject(
    REFCLSID clsid, REFIID iid, void** object)
{
    *object = nullptr;
    if (!Same(clsid, Extension)) return E_NOINTERFACE;
    auto* factory = new (std::nothrow) Factory();
    if (!factory) return E_OUTOFMEMORY;
    const auto result = factory->QueryInterface(iid, object);
    factory->Release();
    return result;
}
