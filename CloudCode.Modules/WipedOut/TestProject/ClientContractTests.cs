using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WipedOut.Online;
using WipedOut.Rules;

namespace WipedOut.Tests;

/// <summary>
/// El contrato entre el servidor y el cliente de Unity. No prueban el cliente (eso necesita Unity), pero
/// sí lo que más se rompe en silencio: que ambos hablen la misma versión del protocolo y que el JSON del
/// servidor encaje con la clase que Unity lee con <c>JsonUtility</c>.
/// </summary>
public class ClientContractTests
{
    private const string Host = "player-host";
    private const string Guest = "player-guest";
    private static readonly DateTime T0 = new(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);

    private static MatchState Playing()
    {
        var state = MatchRules.NewMatch("ABCD", Host, "req", T0);
        MatchRules.StartWithGuest(state, Guest, 4321, T0);
        return state;
    }

    private static string ServerJson(MatchState state, string viewer) =>
        MatchJson.Serialize(MatchRules.BuildView(state, viewer, MatchOutcome.Applied, "", T0));

    private static OnlineMatchView ClientView(MatchState state, string viewer)
    {
        // JsonUtility (Unity) deja las fechas como texto; Newtonsoft por defecto las convertiría.
        var settings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };
        return JsonConvert.DeserializeObject<OnlineMatchView>(ServerJson(state, viewer), settings)!;
    }

    [Test]
    public void ElClienteYElServidorHablanLaMismaVersionDelProtocolo()
    {
        Assert.That(WipedOut.Online.GameProtocol.Version, Is.EqualTo(WipedOut.GameProtocol.Version),
            "hay que subir las dos constantes a la vez cuando cambia un RPC");
    }

    [Test]
    public void LaVistaDelServidorSeLeeEnElModeloDelCliente()
    {
        var view = ClientView(Playing(), Host);

        Assert.That(view.protocolVersion, Is.EqualTo(WipedOut.Online.GameProtocol.Version));
        Assert.That(view.matchCode, Is.EqualTo("ABCD"));
        Assert.That(view.status, Is.EqualTo(OnlineStatus.Playing));
        Assert.That(view.phase, Is.EqualTo(OnlinePhase.Placement));
        Assert.That(view.mySeat, Is.EqualTo(1));
        Assert.That(view.size * view.size, Is.EqualTo(view.cells.Length));
        Assert.That(view.IsMyTurn, Is.True);
        Assert.That(ClientView(Playing(), Guest).IsMyTurn, Is.False);
        Assert.That(view.TurnSecondsLeftAtServerTime(), Is.EqualTo(MatchRules.TurnSeconds).Within(0.5));
    }

    [Test]
    public void TodosLosCamposDelClienteExistenEnElJsonDelServidor()
    {
        // Una vista en colocación, con una mina puesta, trae todos los campos (minas y último evento incluidos).
        var state = Playing();
        MatchRules.ApplyPlay(state, Host, "p1", 0, MoveKind.Place, 4, T0.AddSeconds(1));
        var json = JObject.Parse(ServerJson(state, Host));

        var missing = typeof(OnlineMatchView)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(f => f.Name)
            .Where(name => json.Property(name) == null)
            .ToList();

        Assert.That(missing, Is.Empty,
            "JsonUtility lee por nombre exacto: un campo que cambie en el servidor y no en el cliente quedaría vacío en silencio");
    }

    [Test]
    public void LasMinasSoloLlegonAlClienteCuandoElJuegoLasMuestra()
    {
        var state = Playing();
        MatchRules.ApplyPlay(state, Host, "p1", 0, MoveKind.Place, 0, T0.AddSeconds(1));
        MatchRules.ApplyPlay(state, Host, "p2", 1, MoveKind.Place, 1, T0.AddSeconds(2));
        MatchRules.ApplyPlay(state, Guest, "p3", 2, MoveKind.Place, 2, T0.AddSeconds(3));
        MatchRules.ApplyPlay(state, Guest, "p4", 3, MoveKind.Place, 3, T0.AddSeconds(4));

        Assert.That(ClientView(state, Guest).HasMines, Is.True, "memorización");

        MatchRules.ResolveTime(state, Host, T0.AddSeconds(4 + MatchRules.MemorizeSeconds));
        var searching = ClientView(state, Guest);

        Assert.That(searching.phase, Is.EqualTo(OnlinePhase.Search));
        Assert.That(searching.HasMines, Is.False, "búsqueda: la parte de memoria del juego");
        Assert.That(searching.mineOwners, Is.Null.Or.Empty);
    }

    [Test]
    public void ElAvisoPushEsLivianoYNoLlevaEstado()
    {
        // La forma exacta del payload que envía el módulo (ver WipedOutModule.NotifyOpponentAsync).
        var payload = JsonConvert.SerializeObject(new { type = WipedOutModule.PushMessageType, matchId = "ABCD" });
        var keys = JObject.Parse(payload).Properties().Select(p => p.Name).ToList();

        Assert.That(WipedOutModule.PushMessageType, Is.EqualTo("match_updated"));
        Assert.That(keys, Is.EquivalentTo(new[] { "type", "matchId" }));
    }
}
