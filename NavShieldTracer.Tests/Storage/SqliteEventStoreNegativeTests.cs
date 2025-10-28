using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;
using Xunit;

namespace NavShieldTracer.Tests.Storage;

public sealed class SqliteEventStoreNegativeTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteEventStore _store;

    public SqliteEventStoreNegativeTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"store_tests_{Guid.NewGuid():N}.sqlite");
        _store = new SqliteEventStore(_dbPath);
    }

    [Fact]
    public void ExcluirTesteAtomico_RetornaFalseQuandoNaoExiste()
    {
        var removed = _store.ExcluirTesteAtomico(9999);
        Assert.False(removed);
    }

    [Fact]
    public void AtualizarTesteAtomico_AlteraDadosPersistidos()
    {
        var sessionId = CriarSessaoBasica();
        var testeId = _store.IniciarTesteAtomico(new NovoTesteAtomico("T0001", "Initial", "Descricao"), sessionId);
        _store.FinalizarTesteAtomico(testeId, 0);

        _store.AtualizarTesteAtomico(testeId, numero: "T0999", nome: "Atualizado", descricao: "Nova descricao");

        var teste = _store.ListarTestesAtomicos().First(t => t.Id == testeId);

        Assert.Equal("T0999", teste.Numero);
        Assert.Equal("Atualizado", teste.Nome);
        Assert.Equal("Nova descricao", teste.Descricao);
    }

    [Fact]
    public void ExcluirTesteAtomico_RemoveEventosESessao()
    {
        var sessionId = CriarSessaoBasica();
        var testeId = _store.IniciarTesteAtomico(new NovoTesteAtomico("T1000", "Teste", "Descricao"), sessionId);

        var processo = new EventoProcessoCriado
        {
            EventId = 1,
            EventRecordId = 4001,
            UtcTime = DateTime.UtcNow,
            ProcessId = 6000,
            ParentProcessId = 0,
            Imagem = @"C:\Tools\target.exe",
            LinhaDeComando = "target.exe --run",
            Usuario = "TEST\\User",
            ProcessGuid = Guid.NewGuid().ToString("B")
        };

        _store.InsertEvent(sessionId, processo);
        _store.CompleteSession(sessionId);
        _store.FinalizarTesteAtomico(testeId, 1);

        var removed = _store.ExcluirTesteAtomico(testeId);

        Assert.True(removed);
        Assert.Empty(_store.ListarTestesAtomicos());
        Assert.Equal(0, _store.ContarEventosSessao(sessionId));
    }

    private int CriarSessaoBasica()
    {
        var info = new SessionInfo(
            StartedAt: DateTime.UtcNow,
            TargetProcess: "target.exe",
            RootPid: 5000,
            Host: "host",
            User: "user",
            OsVersion: "Windows");

        return _store.BeginSession(info);
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch
            {
            }
        }
    }
}
