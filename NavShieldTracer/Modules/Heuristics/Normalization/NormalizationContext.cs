using System;
using System.Collections.Generic;
using System.Linq;
using NavShieldTracer.Modules.Storage;

namespace NavShieldTracer.Modules.Heuristics.Normalization
{
    /// <summary>
    /// Encapsula o teste catalogado e os eventos necessários para normalização.
    /// </summary>
    internal class NormalizationContext
    {
        public TesteAtomico Teste { get; }
        public IReadOnlyList<CatalogEventSnapshot> Eventos { get; }

        public NormalizationContext(TesteAtomico teste, IReadOnlyList<CatalogEventSnapshot> eventos)
        {
            Teste = teste ?? throw new ArgumentNullException(nameof(teste));
            Eventos = eventos ?? Array.Empty<CatalogEventSnapshot>();
        }

        /// <summary>
        /// Total de eventos capturados durante a sessão.
        /// </summary>
        public int TotalEventos => Eventos.Count;

        /// <summary>
        /// Lista de IDs de evento distintos presentes na sessão.
        /// </summary>
        public IReadOnlyCollection<int> DistinctEventIds => _distinctEventIds ??= Eventos
            .Select(e => e.EventId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        /// <summary>
        /// Duração da sessão em segundos considerando os timestamps disponíveis.
        /// </summary>
        public double DurationSeconds
        {
            get
            {
                if (_durationSeconds.HasValue)
                {
                    return _durationSeconds.Value;
                }

                var timestamps = Eventos
                    .Select(e => e.UtcTime ?? e.CaptureTime)
                    .Where(t => t.HasValue)
                    .Select(t => t!.Value)
                    .OrderBy(t => t)
                    .ToList();

                if (timestamps.Count < 2)
                {
                    _durationSeconds = 0;
                    return 0;
                }

                _durationSeconds = (timestamps[^1] - timestamps[0]).TotalSeconds;
                return _durationSeconds.Value;
            }
        }

        /// <summary>
        /// Retorna os eventos que correspondem ao Event ID informado.
        /// </summary>
        public IEnumerable<CatalogEventSnapshot> GetEventosPorId(int eventId) =>
            Eventos.Where(e => e.EventId == eventId);

        private IReadOnlyCollection<int>? _distinctEventIds;
        private double? _durationSeconds;
    }
}
