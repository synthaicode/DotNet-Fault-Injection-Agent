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
#include <map>

STDAPI MetaDataGetDispenser(REFCLSID, REFIID, LPVOID*);

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
        const bool sqlTarget = moduleName && _wcsicmp(moduleName, targetName.c_str()) == 0;
        strings->FreeString(moduleName);
        if (!valueTarget && !sqlTarget) return S_OK;

        if (sqlTarget)
        {
            if (!planReady) return S_OK;
            mdMethodDef token;
            result = method->GetMethodToken(&token);
            if (FAILED(result) || plannedRules.find(token) == plannedRules.end()) return result;

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
        mdMethodDef token;
        result = method->GetMethodToken(&token);
        if (FAILED(result)) return result;
        if (planReady && plannedOffsets.find(token) != plannedOffsets.end())
            return RedirectSqlCall(method, graph, first, token);
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
        if (size <= 0 || size > 65536) throw std::runtime_error("invalid plan file size");
        input.seekg(0);

        bool duplicate = false;
        std::vector<std::set<std::string>> keys;
        auto plan = nlohmann::json::parse(input, [&](int, auto event, auto& value)
        {
            if (event == nlohmann::json::parse_event_t::object_start)
                keys.emplace_back();
            if (event == nlohmann::json::parse_event_t::key)
                duplicate |= !keys.back().insert(value.template get<std::string>()).second;
            if (event == nlohmann::json::parse_event_t::object_end)
                keys.pop_back();
            return true;
        });
        if (duplicate || !plan.is_object() || !plan.at("version").is_number_unsigned())
            throw std::runtime_error("invalid plan object");
        const DWORD exeLength = GetModuleFileNameW(nullptr, path, _countof(path));
        if (exeLength == 0 || exeLength >= _countof(path))
            throw std::runtime_error("cannot resolve process executable");
        executable.assign(path, exeLength);
        targetName = std::filesystem::path(executable).filename().wstring();
        if (_wcsicmp(targetName.c_str(), L"OrderAgentProbe.exe") == 0)
        {
            const DWORD targetLength = GetEnvironmentVariableW(L"WINDOWS_CHAOS_SAMPLE_EXE", path, _countof(path));
            if (targetLength == 0 || targetLength >= _countof(path))
                throw std::runtime_error("missing sample executable path");
            executable.assign(path, targetLength);
            targetName = std::filesystem::path(executable).filename().wstring();
            if (!std::filesystem::path(executable).is_absolute() ||
                _wcsicmp(targetName.c_str(), L"MultithreadedOrderInsert.exe") != 0)
                throw std::runtime_error("unsupported sample executable");
            externalSample = true;
        }
        else if (_wcsicmp(targetName.c_str(), L"SqlTarget.exe") != 0)
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
        auto rules = nlohmann::json::array({plan});
        if (plan.at("version") == 2)
        {
            if (plan.size() != 2 || !plan.at("rules").is_array())
                throw std::runtime_error("invalid multi-rule plan");
            rules = plan.at("rules");
        }
        if (rules.empty())
            throw std::runtime_error("expected at least one rule");

        ATL::CComPtr<IMetaDataImport> metadata;
        mdTypeDef owner{};
        PCCOR_SIGNATURE wrapperSignature{};
        ULONG wrapperSignatureSize{};
        if (!externalSample)
        {
            ATL::CComPtr<IMetaDataDispenser> dispenser;
            HRESULT result = MetaDataGetDispenser(CLSID_CorMetaDataDispenser, IID_IMetaDataDispenser,
                reinterpret_cast<void**>(&dispenser));
            if (FAILED(result)) throw std::runtime_error("cannot open metadata dispenser");

            result = dispenser->OpenScopeOnMemory(bytes.data(), static_cast<ULONG>(bytes.size()), ofRead,
                IID_IMetaDataImport, reinterpret_cast<IUnknown**>(&metadata));
            if (FAILED(result)) throw std::runtime_error("cannot read target metadata");

            mdMethodDef selected;
            GUID imageMvid;

            if (FAILED(metadata->GetScopeProps(nullptr, 0, nullptr, &imageMvid)))
                throw std::runtime_error("cannot read target MVID");
            if (FAILED(metadata->FindTypeDefByName(L"Program", mdTokenNil, &owner)) ||
                FAILED(metadata->FindMethod(owner, L"Selected", nullptr, 0, &selected)) ||
                FAILED(metadata->GetMethodProps(selected, nullptr, nullptr, 0, nullptr, nullptr,
                    &wrapperSignature, &wrapperSignatureSize, nullptr, nullptr)))
                throw std::runtime_error("unexpected fixture metadata");

            plannedMvid = imageMvid;
        }
        for (const auto& rule : rules)
        {
            auto [token, runtimeRule] = ValidateRule(rule, hash);
            mdTypeDef declaringType;
            PCCOR_SIGNATURE signature;
            ULONG signatureSize;
            DWORD attributes;
            if (!externalSample && (FAILED(metadata->GetMethodProps(token, &declaringType, nullptr, 0, nullptr,
                    &attributes, &signature, &signatureSize, nullptr, nullptr)) ||
                (declaringType != owner || !IsMdStatic(attributes) ||
                    signatureSize != wrapperSignatureSize ||
                    std::memcmp(signature, wrapperSignature, signatureSize) != 0)))
                throw std::runtime_error("rule target does not match fixture metadata");
            plannedOffsets.emplace(token, rule.at("ilOffset").get<DWORD>());
            if (!plannedRules.emplace(token, std::move(runtimeRule)).second)
                throw std::runtime_error("rules must target distinct methods");
        }

        for (const auto& [token, runtimeRule] : plannedRules)
        {
            const auto key = L"WINDOWS_CHAOS_VALIDATED_RULE_" + std::to_wstring(token);
            if (!SetEnvironmentVariableW(key.c_str(), runtimeRule.c_str()))
                throw std::runtime_error("cannot publish validated rule");
        }

        planReady = true;
        std::fprintf(stderr, "plan accepted: %zu rule(s) validated\n", rules.size());
    }

    std::pair<mdMethodDef, std::wstring> ValidateRule(const nlohmann::json& plan, const std::string& hash)
    {
        if (!plan.is_object() || plan.size() != 11 ||
            !plan.at("version").is_number_unsigned() || plan.at("version") != 1 ||
            !plan.at("ilOffset").is_number_unsigned() || plan.at("ilOffset").get<uint64_t>() > MAXDWORD ||
            (!externalSample && plan.at("ilOffset") != 1) ||
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

        GUID ruleMvid;
        const auto mvid = plan.at("mvid").get<std::string>();
        if (mvid.size() != 36) throw std::runtime_error("invalid MVID");
        const std::wstring guid = L"{" + std::wstring(mvid.begin(), mvid.end()) + L"}";
        if (FAILED(CLSIDFromString(guid.c_str(), &ruleMvid)))
            throw std::runtime_error("invalid MVID");

        if (plan.at("sha256") != hash) throw std::runtime_error("executable SHA-256 mismatch");
        if (externalSample && plannedRules.empty()) plannedMvid = ruleMvid;
        if (plannedMvid != ruleMvid) throw std::runtime_error("rule MVID does not match executable");
        return {token.get<mdMethodDef>(), std::move(runtimeRule)};
    }

    HRESULT RedirectSqlCall(IMethodInfo* method, IInstructionGraph* graph, IInstruction* first, mdMethodDef target)
    {
        const DWORD expected = plannedOffsets.at(target);
        ATL::CComPtr<IInstruction> call(first);
        HRESULT result;
        while (call)
        {
            DWORD offset;
            result = call->GetOriginalOffset(&offset);
            if (FAILED(result)) return result;
            if (offset == expected) break;
            ATL::CComPtr<IInstruction> next;
            result = call->GetNextInstruction(&next);
            if (FAILED(result)) return result;
            call = next;
        }
        if (!call) return E_FAIL;

        ILOrdinalOpcode opcode;
        result = call->GetOpCode(&opcode);
        if (FAILED(result)) return result;
        if (opcode != Cee_Callvirt) return E_FAIL;

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

        mdToken helper;
        if (externalSample)
        {
            result = DefineExternalHelper(module, declaringType, &helper);
        }
        else
        {
            mdTypeDef program;
            result = import->FindTypeDefByName(L"Program", mdTokenNil, &program);
            if (FAILED(result)) return result;
            result = import->FindMethod(program, L"FailBeforeSend", nullptr, 0, &helper);
        }
        if (FAILED(result)) return result;

        ATL::CComPtr<IInstructionFactory> factory;
        result = method->GetInstructionFactory(&factory);
        if (FAILED(result)) return result;

        ATL::CComPtr<IInstruction> replacement;
        result = factory->CreateTokenOperandInstruction(Cee_Call, helper, &replacement);
        if (FAILED(result)) return result;
        ATL::CComPtr<IInstruction> ruleToken;
        result = factory->CreateIntOperandInstruction(Cee_Ldc_I4, static_cast<INT32>(target), &ruleToken);
        if (FAILED(result)) return result;
        result = graph->InsertBefore(call, ruleToken);
        if (FAILED(result)) return result;

        result = graph->Replace(call, replacement);
        if (SUCCEEDED(result)) std::fprintf(stderr, "IL rewritten: methodToken=%u\n", static_cast<unsigned>(target));
        return result;
    }

    static HRESULT DefineExternalHelper(IModuleInfo* module, mdToken commandType, mdToken* helper)
    {
        ATL::CComPtr<IUnknown> metadata;
        HRESULT result = module->GetMetaDataEmit(&metadata);
        if (FAILED(result)) return result;
        ATL::CComQIPtr<IMetaDataEmit, &IID_IMetaDataEmit> emit(metadata);
        ATL::CComQIPtr<IMetaDataAssemblyEmit, &IID_IMetaDataAssemblyEmit> assemblyEmit(metadata);
        if (!emit || !assemblyEmit) return E_NOINTERFACE;

        ASSEMBLYMETADATA identity{};
        identity.usMajorVersion = 1;
        mdAssemblyRef assembly;
        result = assemblyEmit->DefineAssemblyRef(nullptr, 0, L"ChaosOrderBridge", &identity,
            nullptr, 0, 0, &assembly);
        if (FAILED(result)) return result;

        mdTypeRef type;
        result = emit->DefineTypeRefByName(assembly, L"ChaosOrderBridge.Dispatch", &type);
        if (FAILED(result)) return result;

        BYTE signature[9] = {IMAGE_CEE_CS_CALLCONV_DEFAULT, 2, ELEMENT_TYPE_I4, ELEMENT_TYPE_CLASS};
        const auto length = CorSigCompressToken(commandType, signature + 4);
        signature[4 + length] = ELEMENT_TYPE_I4;
        return emit->DefineMemberRef(type, L"ExecuteNonQuery", signature, 5 + length, helper);
    }

    std::wstring targetName;
    bool externalSample = false;
    std::map<mdMethodDef, DWORD> plannedOffsets;
    ATL::CComPtr<IProfilerStringManager> strings;
    std::wstring executable;
    GUID plannedMvid{};
    std::map<mdMethodDef, std::wstring> plannedRules;
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
