# Getting Started

Esta secao resume os passos minimos para colocar o NavShieldTracer em operacao.

## Requisitos de Sistema
- Windows 10 1809 ou superior (build 17763)
- .NET 9 SDK ou runtime instalado
- Permissao de administrador para ler o log Sysmon
- Sysmon instalado com uma configuracao alinhada ao projeto (veja abaixo)

## Instalar e Configurar o Sysmon
```powershell
# Aceite a licenca e instale o Sysmon
sysmon.exe -accepteula -i

# Aplique a configuracao recomendada
sysmon.exe -c sysmon-config-completa.xml
```

> Use um console elevado para executar esses comandos. O arquivo `sysmon-config-completa.xml` reside na raiz do repositorio e acompanha filtros pensados para laboratorio forense.

## Clonar, Compilar e Validar
```powershell
git clone https://github.com/seu-usuario/NavShieldTracer.git
cd NavShieldTracer

dotnet build NavShieldTracer.sln
```

A solucao possui dois projetos: `NavShieldTracer` (coletor) e `TesteSoftware` (suporte aos testes atômicos). O build garante que as dependencias foram restauradas.

## Executar o Coletor
```powershell
# Sempre em um console com privilegios
cd NavShieldTracer

dotnet run --project NavShieldTracer/NavShieldTracer.csproj
```

O menu principal advierte quando o Sysmon nao esta disponivel. Escolha o processo alvo ou aguarde um novo processo conforme o modo desejado.

## Habilitar a Documentacao Local
```powershell
# a partir da raiz do repositorio
dotnet tool restore

dotnet tool run docfx docs/docfx.json

dotnet tool run docfx serve docs/_site --port 8080
```

Acesse `http://localhost:8080` para navegar pela documentacao. Gere novamente sempre que atualizar comentarios XML ou markdown para manter o site alinhado ao codigo.
