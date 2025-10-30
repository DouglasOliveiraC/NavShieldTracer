using NavShieldTracer.Modules.Models;
using NavShieldTracer.Storage;

namespace NavShieldTracer.Modules.Heuristics.Normalization
{
    /// <summary>
    /// Conduz a normalização após a catalogação, guiando o analista pelo CLI.
    /// </summary>
    internal class CatalogNormalizationWorkflow
    {
        private readonly SqliteEventStore _store;
        private readonly CatalogNormalizer _normalizer;

        internal CatalogNormalizationWorkflow(SqliteEventStore store, CatalogNormalizer? normalizer = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _normalizer = normalizer ?? new CatalogNormalizer();
        }

        public void Executar(int testeId)
        {
            var teste = _store.ObterTesteAtomico(testeId);
            if (teste == null)
            {
                Console.WriteLine($" Teste ID {testeId} não localizado para normalização.");
                return;
            }

            var eventos = _store.ObterEventosDaSessao(teste.SessionId);
            var contexto = new NormalizationContext(teste, eventos);
            var resultado = _normalizer.Normalize(contexto);

            ApresentarResumo(teste, resultado);
            var resultadoAjustado = ColetarFeedbackUsuario(resultado);

            _store.SalvarResultadoNormalizacao(resultadoAjustado);

            Console.WriteLine("\n Normalização registrada com sucesso.");
            Console.WriteLine($"   - Tarja final: {resultadoAjustado.Signature.Severity}");
        }

        private void ApresentarResumo(TesteAtomico teste, CatalogNormalizationResult resultado)
        {
            Console.WriteLine("\n===========================================");
            Console.WriteLine($"NORMALIZAÇÃO DO TESTE {teste.Numero} - {teste.Nome}");
            Console.WriteLine("===========================================");
            Console.WriteLine($"Eventos totais: {resultado.Quality.TotalEvents}");
            Console.WriteLine($"Eventos core:   {resultado.Segregation.CoreEvents.Count}");
            Console.WriteLine($"Eventos suporte:{resultado.Segregation.SupportEvents.Count}");
            Console.WriteLine($"Eventos ruído:  {resultado.Segregation.NoiseEvents.Count}");
            Console.WriteLine($"Cobertura core: {resultado.Quality.CoveragePercentual:F1}%");
            Console.WriteLine($"Duração sessão: {resultado.Signature.FeatureVector.TemporalSpanSeconds:F1}s");
            Console.WriteLine($"Tarja sugerida: {resultado.Signature.Severity} ({resultado.Signature.SeverityReason})");

            if (resultado.Quality.Warnings.Count > 0)
            {
                Console.WriteLine("\nAvisos:");
                foreach (var warning in resultado.Quality.Warnings)
                {
                    Console.WriteLine($"  - {warning}");
                }
            }
        }

        private CatalogNormalizationResult ColetarFeedbackUsuario(CatalogNormalizationResult resultado)
        {
            var severidade = AjustarSeveridade(resultado.Signature);

            var logs = resultado.Logs.ToList();
            logs.Add(new NormalizationLogEntry("MANUAL", "INFO",
                $"Analista confirmou tarja {severidade.Severity} com razão '{severidade.Reason}'."));

            var assinaturaAtualizada = resultado.Signature with
            {
                Severity = severidade.Severity,
                SeverityReason = severidade.Reason
            };

            return resultado with
            {
                Signature = assinaturaAtualizada,
                Logs = logs
            };
        }

        private SeverityDecision AjustarSeveridade(NormalizedTestSignature assinatura)
        {
            Console.WriteLine("\nDefinição da tarja do teste:");
            Console.WriteLine($"Sugestão atual: {assinatura.Severity} ({assinatura.SeverityReason})");
            Console.WriteLine("Pressione ENTER para manter ou escolha uma tarja:");
            Console.WriteLine("  1 - Verde    (sem risco)");
            Console.WriteLine("  2 - Amarela  (atenção)");
            Console.WriteLine("  3 - Laranja  (alto risco)");
            Console.WriteLine("  4 - Vermelho (ameaça crítica)");
            Console.Write("> ");

            var entrada = Console.ReadLine();
            ThreatSeverityTarja novaTarja = assinatura.Severity;

            if (!string.IsNullOrWhiteSpace(entrada))
            {
                novaTarja = entrada.Trim() switch
                {
                    "1" => ThreatSeverityTarja.Verde,
                    "2" => ThreatSeverityTarja.Amarelo,
                    "3" => ThreatSeverityTarja.Laranja,
                    "4" => ThreatSeverityTarja.Vermelho,
                    _ => assinatura.Severity
                };
            }

            Console.Write("Justifique a tarja (ENTER para manter justificativa atual): ");
            var justificativa = Console.ReadLine();
            var reason = string.IsNullOrWhiteSpace(justificativa)
                ? assinatura.SeverityReason
                : justificativa.Trim();

            return new SeverityDecision(novaTarja, reason);
        }

        private static bool PerguntarSimNao(string prompt, bool valorPadrao)
        {
            while (true)
            {
                var sufixo = valorPadrao ? "[S/n]" : "[s/N]";
                Console.Write($"{prompt} {sufixo}: ");
                var entrada = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(entrada))
                {
                    return valorPadrao;
                }

                entrada = entrada.Trim().ToLowerInvariant();
                if (entrada is "s" or "sim")
                {
                    return true;
                }

                if (entrada is "n" or "nao" or "não")
                {
                    return false;
                }

                Console.WriteLine("Entrada inválida. Use S ou N.");
            }
        }
    }
}
