#include <atlbase.h>

#include <atlcom.h>
#include <cor.h>
#include <InstrumentationEngine.h>
#include <cwchar>

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
        return manager->QueryInterface(IID_PPV_ARGS(&strings));
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

        BSTR name = nullptr;
        result = method->GetFullName(&name);
        if (FAILED(result)) return result;
        *instrument = name && wcscmp(name, valueTarget ? L"Program.GetValue" : L"Program.Selected") == 0;
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
        result = import->GetMemberRefProps(original, &declaringType, memberName, _countof(memberName),
            nullptr, nullptr, nullptr);
        if (FAILED(result)) return result;
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
        return graph->Replace(call, replacement);
    }

    ATL::CComPtr<IProfilerStringManager> strings;
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
