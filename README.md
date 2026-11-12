# Otimizador PC PRO 3.2

App de limpeza e manutenção para Windows 10/11 (x64), em C# / Windows Forms (.NET 8).
Reescrito do zero a partir da versão 2: **leve (≈ 0,6 MB)**, sem nenhum pacote NuGet, com interface
moderna (tema claro/escuro) e com todas as funções reais e reversíveis.

## Estrutura

```
OtimizadorPCPro.sln
src/Otimizador/              ← o aplicativo
  Core/                      ← configurações, log, licença, execução de comandos, métricas
  Features/                  ← limpeza, memória, inicialização, programas, duplicados,
                               privacidade, ajustes reversíveis, ferramentas, agendador
  UI/                        ← tema, controles desenhados, janela principal, diálogos
  UI/Pages/                  ← uma classe por página
  Resources/                 ← Termos.txt e Ajuda.txt (embutidos no .exe)
tests/Otimizador.Tests/      ← testes automáticos (licença, versões, limpeza de pastas)
tools/GeradorLicencas/       ← ferramenta do VENDEDOR para emitir chaves (não distribuir)
instalador/setup.iss         ← instalador Inno Setup (baixa o .NET 8 se faltar)
Publicar.bat                 ← gera publicar\OtimizadorPCPro.exe
CriarInstalador.bat          ← gera saida\OtimizadorPCPro_Setup_3.2.0.exe
publicar/OtimizadorPCPro.exe ← executável já compilado (pronto para testar)
```

## Requisitos

* Para **usar**: Windows 10 1809+ ou 11 (64 bits) e o **.NET 8 Desktop Runtime**
  (o instalador baixa e instala automaticamente se faltar).
* Para **compilar**: .NET SDK 8 (`winget install Microsoft.DotNet.SDK.8`) e, para o instalador, Inno Setup 6.

## Compilar e publicar

```bat
Publicar.bat              :: versão leve (~0,6 MB, usa o .NET 8 instalado)
Publicar.bat autonomo     :: versão autônoma (~70 MB, não precisa do .NET)
CriarInstalador.bat       :: instalador em saida\
```

Ou abra `OtimizadorPCPro.sln` no Visual Studio 2022 e rode o projeto **Otimizador**.

## Testes automáticos

```bat
dotnet run --project tests\Otimizador.Tests
```

Sem nenhum pacote externo. Cobrem a leitura e a assinatura das licenças (inclusive chaves adulteradas e falsas),
a comparação de versões, a leitura da resposta do GitHub e a varredura segura de pastas (não segue atalhos, não apaga
pastas novas). A cada envio ao GitHub, o fluxo `.github/workflows/ci.yml` compila o app e roda esses testes.

## Atualizações

Uma vez por dia o app consulta `api.github.com/repos/corretoresdabahiacom-create/otimizador/releases/latest`
(só a versão publicada, nenhum dado do usuário é enviado). Se houver versão maior, mostra um aviso e, em
Configurações > Atualizações, abre a página de download. Nada é baixado nem instalado sozinho. O usuário pode desligar
a opção. Para lançar uma versão: aumente `<Version>` no `Otimizador.csproj` e em `instalador/setup.iss`, gere o
instalador e crie uma Release no GitHub com a tag `vX.Y.Z` e o instalador anexado.

## Licenças (importante)

A versão anterior aceitava uma "chave master" escrita no próprio LEIA-ME e nas mensagens de erro.
Agora as chaves são **assinadas digitalmente (ECDSA P-256)**: o app só tem a chave pública, então
ninguém consegue criar uma chave válida olhando o executável. A ativação funciona sem internet.

Emitir uma chave (na pasta `tools/GeradorLicencas`):

```bat
dotnet run -- gerar --titular "cliente@email.com" --dias 30     :: assinatura mensal
dotnet run -- gerar --titular "Empresa X" --vitalicia           :: vitalícia
dotnet run -- verificar <CHAVE>                                  :: confere uma chave
```

* A chave privada fica em `tools/GeradorLicencas/chave-privada.pem`. **Guarde em local seguro, nunca
  distribua nem envie ao GitHub** (o `.gitignore` já a exclui). Quem tiver esse arquivo emite licenças.
* Para trocar o par de chaves: `dotnet run -- novo-par --forcar` e recompile o app
  (as chaves emitidas antes deixam de valer).
* Assinaturas: aviso 3 dias antes do vencimento e 5 dias de carência; depois o app pede uma nova chave.
* Quem usava a chave master antiga precisa receber uma chave nova.

## O que o app guarda e onde

* Configurações: `%LocalAppData%\OtimizadorPRO\config.json`
* Registro de atividades: `%LocalAppData%\OtimizadorPRO\logs\otimizador.log`
* "Iniciar com o Windows": tarefa agendada `OtimizadorPCPro` (necessária porque o app roda como administrador)
* Nenhum dado do usuário é enviado para a internet. A única conexão é a consulta opcional de novas versões (veja "Atualizações").

## Funções

| Página | O que faz |
|---|---|
| Painel | Nota de saúde, métricas (CPU, memória, disco, tempo ligado), "Otimizar agora", manutenção automática |
| Limpeza | Analisa e limpa temporários (>24 h), relatórios de erro, cache do Windows Update, logs antigos, miniaturas, cache de navegadores fechados e Lixeira |
| Desempenho | Modo Gamer, Modo Economia (notebook), ajustes individuais reversíveis, liberar memória |
| Inicialização | Ativa/desativa programas de inicialização (mesmo método do Gerenciador de Tarefas) |
| Programas | Lista e desinstala programas pelo desinstalador oficial |
| Duplicados | Comparação real de conteúdo (SHA-256); remove para a Lixeira, nunca todas as cópias |
| Privacidade | Limpa rastros de uso e aplica ajustes de privacidade do Windows (reversíveis) |
| Ferramentas | Proteção do Windows, otimizar unidades, CHKDSK, DISM+SFC, ponto de restauração, DNS, rede |
| Agendamentos | Tarefas diárias/semanais/mensais salvas em disco |
| Configurações | Tema, inicialização, manutenção, licença, desfazer ajustes, ajuda, termos, suporte |

Detalhes da auditoria e de tudo que foi corrigido/removido: veja `AUDITORIA.md`.
