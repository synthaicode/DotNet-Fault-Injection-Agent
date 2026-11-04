#include <atlbase.h>

#include <atlcom.h>
#include <cor.h>
#include <InstrumentationEngine.h>
#include <cwchar>
#include <cstring>
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <nlohmann/json.hpp>
#include <cstdio>
#include <set>

class RewriteModule : public ATL::CAtlDllModuleT<RewriteModule> {};
static RewriteModule module;

static const CLSID RewriteClass =
    {0x9f919afe, 0xc6f4, 0x4263, {0xa1, 0x12, 0x8d, 0xec, 0x99, 0x47, 0xbc, 0x1e}};

class RewriteMethod : public ATL::CComObjectRootEx<ATL::CComMultiThreadModel>,
                      public IInstrumentationMethod
{
public:
    BEGIN_COM_MAP(RewriteMethod)
        COM_INTERFACE_ENTRY(IInstrumentationMethod)
    END_COM_MAP()

    HRESULT STDMETHODCALLTYPE Initialize(IProfilerManager* manager) override
    {
        const HRESULT result = manager->QueryInterface(IID_PPV_ARGS(&strings));
        if (FAILED(result)) return result;
        try
        {
            LoadPlan();
        }
        catch (const std::exception& error)
        {
            std::fprintf(stderr, "plan rejected: %s\n", error.what());
        }
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE ShouldInstrumentMethod(IMethodInfo* method, BOOL rejit, BOOL* instrument) override
    {
        *instrument = FALSE;
        if (rejit) return S_OK;

        ATL::CComPtr<IModuleInfo> module;
        HRESULT result = method->GetModuleInfo(&module);
        if (FAILED(result)) return result;

        BSTR moduleName = nullptr;
        result = module->GetModuleName(&moduleName);
        if (FAILED(result)) return result;
        const bool valueTarget = moduleName && wcscmp(moduleName, L"ProfilerTarget.exe") == 0;
        const bool sqlTarget = moduleName && wcscmp(moduleName, L"SqlTarget.exe") == 0;
        strings->FreeString(moduleName);
        if (!valueTarget && !sqlTarget) return S_OK;

        if (sqlTarget)
        {
            if (!planReady) return S_OK;
            mdMethodDef token;
            result = method->GetMethodToken(&token);
            if (FAILED(result) || token != plannedMethod) return result;

            GUID mvid;
            result = module->GetMVID(&mvid);
            if (FAILED(result)) return result;
            if (mvid != plannedMvid)
            {
                std::fprintf(stderr, "plan rejected: loaded module MVID mismatch\n");
                return S_OK;
            }

            BSTR path = nullptr;
            result = module->GetFullPath(&path);
            if (FAILED(result)) return result;
            *instrument = path && _wcsicmp(path, executable.c_str()) == 0;
            strings->FreeString(path);
            return S_OK;
        }

        BSTR name = nullptr;
        result = method->GetFullName(&name);
        if (FAILED(result)) return result;
        *instrument = name && wcscmp(name, L"Program.GetValue") == 0;
        strings->FreeString(name);
        return S_OK;
    }

    HRESULT STDMETHODCALLTYPE InstrumentMethod(IMethodInfo* method, BOOL) override
    {
        ATL::CComPtr<IInstructionGraph> graph;
        HRESULT result = method->GetInstructions(&graph);
        if (FAILED(result)) return result;

        ATL::CComPtr<IInstruction> first;
        result = graph->GetFirstInstruction(&first);
        if (FAILED(result)) return result;
        if (!first) return E_FAIL;

        ILOrdinalOpcode opcode;
        result = first->GetOpCode(&opcode);
        if (FAILED(result)) return result;
        if (opcode == Cee_Ldarg_0) return RedirectSqlCall(method, graph, first);
        if (opcode != Cee_Ldc_I4_S) return E_FAIL;

        ATL::CComPtr<IInstructionFactory> factory;
        result = method->GetInstructionFactory(&factory);
        if (FAILED(result)) return result;

        ATL::CComPtr<IInstruction> replacement;
        result = factory->CreateInstruction(Cee_Ldc_I4_M1, &replacement);
        if (FAILED(result)) return result;
        return graph->Replace(first, replacement);
    }

    HRESULT STDMETHODCALLTYPE OnAppDomainCreated(IAppDomainInfo*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnAppDomainShutdown(IAppDomainInfo*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnAssemblyLoaded(IAssemblyInfo*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnAssemblyUnloaded(IAssemblyInfo*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnModuleLoaded(IModuleInfo*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnModuleUnloaded(IModuleInfo*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnShutdown() override { return S_OK; }
    HRESULT STDMETHODCALLTYPE BeforeInstrumentMethod(IMethodInfo*, BOOL) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnInstrumentationComplete(IMethodInfo*, BOOL) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE AllowInlineSite(IMethodInfo*, IMethodInfo*, BOOL* allow) override
    {
        *allow = TRUE;
        return S_OK;
    }

private:
    void LoadPlan()
    {
        wchar_t path[32768];
        const DWORD length = GetEnvironmentVariableW(L"WINDOWS_CHAOS_PLAN", path, _countof(path));
        if (length == 0) return;
        if (length >= _countof(path)) throw std::runtime_error("plan path too long");

        std::ifstream input{std::filesystem::path(path), std::ios::binary | std::ios::ate};
        const auto size = input.tellg();
        if (size <= 0 || size > 8192) throw std::runtime_error("invalid plan file size");
        input.seekg(0);

        bool duplicate = false;
        std::set<std::string> keys;
        auto plan = nlohmann::json::parse(input, [&](int depth, auto event, auto& value)
        {
            if (depth == 1 && event == nlohmann::json::parse_event_t::key)
                duplicate |= !keys.insert(value.template get<std::string>()).second;
            return true;
        });
        if (duplicate || !plan.is_object() || plan.size() != 11 ||
            !plan.at("version").is_number_unsigned() || plan.at("version") != 1 ||
            !plan.at("ilOffset").is_number_unsigned() || plan.at("ilOffset") != 1 ||
            plan.at("call") != "System.Data.Common.DbCommand.ExecuteNonQuery()" ||
            plan.at("fault") != "timeout-before-send")
            throw std::runtime_error("unsupported plan fields or values");

        const auto limit = plan.at("maxFirings");
        if (!limit.is_number_unsigned() || limit.get<uint64_t>() > INT_MAX)
            throw std::runtime_error("invalid maxFirings");
        std::wstring runtimeRule;
        for (const auto key : {"server", "database", "procedure"})
        {
            const auto value = plan.at(key).get<std::string>();
            if (value.empty() || value.size() > 1024 ||
                value.find_first_of("\r\n") != std::string::npos || value.find('\0') != std::string::npos)
                throw std::runtime_error("invalid rule condition");

            wchar_t decoded[1024];
            const int count = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
                static_cast<int>(value.size()), decoded, _countof(decoded));
            if (count == 0) throw std::runtime_error("invalid rule encoding");
            runtimeRule.append(decoded, count);
            runtimeRule += L'\n';
        }
        runtimeRule += std::to_wstring(limit.get<int>());

        const auto token = plan.at("methodToken");
        if (!token.is_number_unsigned() || token.get<uint64_t>() < 0x06000001 ||
            token.get<uint64_t>() > 0x06ffffff)
            throw std::runtime_error("invalid method token");

        const auto mvid = plan.at("mvid").get<std::string>();
        if (mvid.size() != 36) throw std::runtime_error("invalid MVID");
        const std::wstring guid = L"{" + std::wstring(mvid.begin(), mvid.end()) + L"}";
        if (FAILED(CLSIDFromString(guid.c_str(), &plannedMvid)))
            throw std::runtime_error("invalid MVID");

        const DWORD exeLength = GetModuleFileNameW(nullptr, path, _countof(path));
        if (exeLength == 0 || exeLength >= _countof(path))
            throw std::runtime_error("cannot resolve process executable");
        executable.assign(path, exeLength);
        if (_wcsicmp(std::filesystem::path(executable).filename().c_str(), L"SqlTarget.exe") != 0)
            throw std::runtime_error("unsupported process executable");

        std::ifstream image{std::filesystem::path(executable), std::ios::binary | std::ios::ate};
        const auto imageSize = image.tellg();
        if (imageSize <= 0 || imageSize > 16 * 1024 * 1024)
            throw std::runtime_error("invalid executable size");
        std::vector<unsigned char> bytes(static_cast<size_t>(imageSize));
        image.seekg(0);
        if (!image.read(reinterpret_cast<char*>(bytes.data()), bytes.size()))
            throw std::runtime_error("cannot read executable");

        BCRYPT_ALG_HANDLE algorithm;
        if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0)
            throw std::runtime_error("cannot initialize SHA-256");
        unsigned char digest[32];
        const auto status = BCryptHash(algorithm, nullptr, 0, bytes.data(),
            static_cast<ULONG>(bytes.size()), digest, sizeof(digest));
        BCryptCloseAlgorithmProvider(algorithm, 0);
        if (status != 0) throw std::runtime_error("cannot hash executable");

        std::string hash;
        for (const auto byte : digest)
        {
            hash += "0123456789ABCDEF"[byte >> 4];
            hash += "0123456789ABCDEF"[byte & 15];
        }
        if (plan.at("sha256") != hash) throw std::runtime_error("executable SHA-256 mismatch");

        if (!SetEnvironmentVariableW(L"WINDOWS_CHAOS_VALIDATED_RULE", runtimeRule.c_str()))
            throw std::runtime_error("cannot publish validated rule");

        plannedMethod = token.get<mdMethodDef>();
        planReady = true;
        std::fprintf(stderr, "plan accepted: executable and rule validated\n");
    }

    static HRESULT RedirectSqlCall(IMethodInfo* method, IInstructionGraph* graph, IInstruction* first)
    {
        ATL::CComPtr<IInstruction> call;
        HRESULT result = first->GetNextInstruction(&call);
        if (FAILED(result)) return result;
        if (!call) return E_FAIL;

        ILOrdinalOpcode opcode;
        result = call->GetOpCode(&opcode);
        if (FAILED(result)) return result;
        if (opcode != Cee_Callvirt) return E_FAIL;
        DWORD offset;
        result = call->GetOriginalOffset(&offset);
        if (FAILED(result)) return result;
        if (offset != 1) return E_FAIL;

        ATL::CComPtr<IModuleInfo> module;
        result = method->GetModuleInfo(&module);
        if (FAILED(result)) return result;

        ATL::CComPtr<IUnknown> metadata;
        result = module->GetMetaDataImport(&metadata);
        if (FAILED(result)) return result;
        ATL::CComQIPtr<IMetaDataImport, &IID_IMetaDataImport> import(metadata);
        if (!import) return E_NOINTERFACE;

        ATL::CComQIPtr<IOperandInstruction> operand(call);
        if (!operand) return E_NOINTERFACE;
        mdMemberRef original;
        result = operand->GetOperandValue(sizeof(original), reinterpret_cast<BYTE*>(&original));
        if (FAILED(result)) return result;
        if (TypeFromToken(original) != mdtMemberRef) return E_FAIL;

        mdToken declaringType;
        WCHAR memberName[64];
        PCCOR_SIGNATURE signature;
        ULONG signatureSize;
        result = import->GetMemberRefProps(original, &declaringType, memberName, _countof(memberName),
            nullptr, &signature, &signatureSize);
        if (FAILED(result)) return result;
        const BYTE expectedSignature[] = {0x20, 0, 0x08};
        if (signatureSize != sizeof(expectedSignature) ||
            std::memcmp(signature, expectedSignature, sizeof(expectedSignature)) != 0)
            return E_FAIL;
        if (wcscmp(memberName, L"ExecuteNonQuery") != 0 || TypeFromToken(declaringType) != mdtTypeRef)
            return E_FAIL;

        WCHAR typeName[64];
        result = import->GetTypeRefProps(declaringType, nullptr, typeName, _countof(typeName), nullptr);
        if (FAILED(result)) return result;
        if (wcscmp(typeName, L"System.Data.Common.DbCommand") != 0) return E_FAIL;

        mdTypeDef program;
        result = import->FindTypeDefByName(L"Program", mdTokenNil, &program);
        if (FAILED(result)) return result;

        mdMethodDef helper;
        result = import->FindMethod(program, L"FailBeforeSend", nullptr, 0, &helper);
        if (FAILED(result)) return result;

        ATL::CComPtr<IInstructionFactory> factory;
        result = method->GetInstructionFactory(&factory);
        if (FAILED(result)) return result;

        ATL::CComPtr<IInstruction> replacement;
        result = factory->CreateTokenOperandInstruction(Cee_Call, helper, &replacement);
        if (FAILED(result)) return result;
        result = graph->Replace(call, replacement);
        if (SUCCEEDED(result)) std::fprintf(stderr, "IL rewritten: SQL call redirected\n");
        return result;
    }

    ATL::CComPtr<IProfilerStringManager> strings;
    std::wstring executable;
    GUID plannedMvid{};
    mdMethodDef plannedMethod{};
    bool planReady = false;
};

class RewriteFactory : public ATL::CComObjectRootEx<ATL::CComMultiThreadModel>,
                       public IClassFactory
{
public:
    BEGIN_COM_MAP(RewriteFactory)
        COM_INTERFACE_ENTRY(IClassFactory)
    END_COM_MAP()

    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** object) override
    {
        *object = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        return ATL::CComCreator<ATL::CComObject<RewriteMethod>>::CreateInstance(nullptr, iid, object);
    }

    HRESULT STDMETHODCALLTYPE LockServer(BOOL) override { return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID clsid, REFIID iid, void** object)
{
    *object = nullptr;
    if (clsid != RewriteClass) return CLASS_E_CLASSNOTAVAILABLE;
    return ATL::CComCreator<ATL::CComObject<RewriteFactory>>::CreateInstance(nullptr, iid, object);
}
