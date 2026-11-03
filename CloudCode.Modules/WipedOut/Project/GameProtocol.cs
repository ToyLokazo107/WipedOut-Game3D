namespace WipedOut;

/// <summary>
/// Version del contrato de los RPC (parametros y forma de la vista). Tiene un gemelo en el cliente:
/// <c>WipedOut-Game3D/Assets/Scripts/Online/GameProtocol.cs</c>. No se comparte como codigo fuente
/// (el modulo solo compila <c>Assets/Scripts/Rules</c>), asi que hay que subir las DOS a la vez cada
/// vez que cambie un RPC. Cada vista lleva esta version; si el cliente ve otra distinta, avisa de que
/// hay que actualizar el juego en lugar de interpretar mal la respuesta.
/// </summary>
public static class GameProtocol
{
    public const int Version = 1;
}
