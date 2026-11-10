# Auditoria — Otimizador PC PRO (versão 2 → 3.1)

## Resumo

* **Estado geral:** o app compilava, mas boa parte das funções era **simulada** (mostrava "sucesso" sem fazer nada) e várias rotinas reais causavam dano ao sistema.
* **Maior desperdício:** um executável de **188 MB** (ML.NET, ScottPlot, SQLite, LibreHardwareMonitor, nenhum deles usado de fato) e um pacote de **400 MB** com 4 cópias do executável e 12 arquivos de backup.
* **Maior risco:** recusar os termos rodava `rmdir /s /q` na pasta onde estava o .exe (se fosse Downloads ou Área de Trabalho, ela inteira era apagada). E a "chave master" de licença estava escrita no LEIA-ME e nas próprias mensagens de erro.

## Números (antes → depois)

| Item | Antes | Depois |
|---|---|---|
| Pacote entregue | 400 MB (.rar) | < 1 MB |
| Executável | 188 MB (autônomo) | **0,57 MB** (usa o .NET 8 Desktop Runtime) |
| Pacotes NuGet | 8 | **0** |
| Arquivos .cs | 81 (+12 backups) | 41 |
| Linhas de código | 14.855 | 6.585 |
| `MainForm.cs` | 4.204 linhas com todas as telas | 500 linhas; cada página em seu próprio arquivo |
| Funções simuladas | 11 | 0 |
| Avisos do compilador | suprimidos com `NoWarn` (10 códigos) | 0 avisos, 0 erros, sem supressões |
| Monitoramento | WMI + PerformanceCounter a cada 1–15 s, vários timers que nunca paravam | APIs nativas, 1 timer que só roda com o Painel visível |

## Notas por área (0 = crítico, 3 = bom)

| Área | Antes | Justificativa |
|---|---|---|
| Segurança | 0 | Chave master exposta; `LicenseValid=true` no registro liberava o app; certificado `.pfx` e `shell32.dll` copiados no pacote; desinstalação apagava a pasta do usuário |
| Funções reais | 0 | Modo Gamer, Otimizar Registro, Melhorar Internet, Privacidade, Duplicados, Drivers, Backup, Restaurar, Desfazer e "Modo Extremo" eram simulações |
| Segurança do sistema | 0 | A cada 10 min: regras de firewall duplicadas, bloqueio das portas 445/3389, `LargeSystemCache=1`, prioridade alta do próprio app, apagava cache dos navegadores |
| Correção | 1 | Todas as quebras de linha das mensagens e logs foram perdidas (`\n` sumiu); `Process.Start("mailto:")` falha no .NET 8; MessageBox em thread de fundo; "Iniciar com o Windows" nunca funcionou com app elevado |
| Leveza | 0 | 188 MB, bibliotecas pesadas sem uso, WMI em timers, timers criados e nunca descartados |
| Interface | 1 | Coordenadas fixas, textos sobrepostos, cores de texto escuro sobre fundo escuro no modo escuro, rótulos trocados por "⚡ Otimizador PC PRO" por um localizar/substituir |
| Manutenção | 0 | 3 `.csproj` na mesma pasta (o `dotnet build` falhava), arquivos duplicados em namespaces diferentes, pasta `Disabled` compilada junto |

## Correções aplicadas (em ordem de prioridade)

### Segurança
1. **Desinstalação destrutiva removida.** Recusar os termos agora só fecha o app. A desinstalação usa o desinstalador oficial do Windows.
2. **Licença reescrita com assinatura digital** (ECDSA P-256). Sem chave master, sem chaves fixas ("TESTE-2026-OK", "FREE-2026-TRIAL"), sem flag no registro. Chaves geradas por `tools/GeradorLicencas`. Validade e carência embutidas na chave; atrasar o relógio não estende a validade.
3. **Removidos do pacote:** `OtimizadorPRO.pfx` (certificado de assinatura — trate como comprometido e gere outro), `shell32.dll` e `temp.ico` (cópias de 7,8 MB do Windows), executáveis e instaladores antigos.
4. **Comandos sem `cmd /c`** e sempre pelo caminho completo em System32 (evita injeção e execução de programas falsos do PATH).
5. **Limpeza segura:** não segue junções/links simbólicos, só apaga temporários com mais de 24 h, pula arquivos em uso, não limpa cache de navegador aberto, não limpa Prefetch.

### Danos ao sistema corrigidos
6. **Removido o "Modo IA"** (não havia IA): a cada 10–20 minutos ele recriava regras de firewall (a lista crescia sem parar), bloqueava compartilhamento de arquivos (445) e Área de Trabalho Remota (3389), gravava `LargeSystemCache=1` e `DisablePagingExecutive=1`, apagava o cache dos navegadores e oferecia reiniciar o PC. Substituído por **Manutenção automática** honesta (temporários, relatórios de erro e logs; memória só acima de 85%; padrão a cada 6 h).
7. **Removido o "Modo Extremo"**: desativava o Windows Search, removia apps Xbox/Bing, trocava o DNS de adaptadores com nome fixo ("Ethernet"/"Wi-Fi"), sem desfazer.
8. **Removido o "Modo Enterprise"**: exportava e reimportava **todo o HKLM** como "backup".
9. **Removido o "Boot Atrasado"**: criava um VBS oculto que só iniciava o serviço de horário após 5 min.
10. **Migração automática** na primeira execução da 3.1: remove as regras `BlockPort*`, restaura `LargeSystemCache`/`DisablePagingExecutive`, apaga o VBS e as entradas antigas de inicialização e licença (tudo registrado no log).

### Funções que eram simuladas e agora funcionam
11. Modo Gamer (plano de energia, Modo de Jogo, Game DVR, prioridade, fechar apps escolhidos, memória — e desfaz tudo ao desativar).
12. Modo Economia/notebook (antes também ativava "desempenho máximo" da NVIDIA, o oposto de economizar).
13. Liberar memória (antes limpava só a memória do próprio otimizador e mostrava esse número).
14. Localizador de duplicados (comparação de conteúdo com SHA-256; envia para a Lixeira; nunca remove todas as cópias; ignora pastas do sistema).
15. Limpeza de privacidade (recentes, listas de atalhos, Executar, Explorador, área de transferência).
16. Gerenciador de inicialização (antes "desativar" **apagava** a entrada; agora usa o mesmo método do Gerenciador de Tarefas e é reversível).
17. Desinstalador (antes só abria o Painel de Controle; agora executa o desinstalador do programa, inclusive MSI).
18. Ajustes reversíveis com "Desfazer" exato (substitui Backup/Restaurar/Desfazer que eram falsos).
19. Agendamentos salvos em disco (antes eram perdidos ao fechar) e executados mesmo se o horário passou com o PC desligado.
20. "Iniciar com o Windows" via Agendador de Tarefas (a chave Run não inicia apps que exigem administrador).

### Leveza e qualidade
21. Zero dependências externas; ícones pela fonte do Windows (sem imagens); páginas criadas sob demanda.
22. Métricas por `GetSystemTimes`/`GlobalMemoryStatusEx` em vez de WMI/PerformanceCounter.
23. Instância única, log com rotação, tratamento global de erros, configuração gravada de forma atômica.
24. Interface nova: barra lateral, cartões, tema claro/escuro/sistema, layout responsivo, rolagem pela roda do mouse, DPI do sistema.

## O que foi mantido
* Nome, e-mail de suporte, texto dos Termos de uso (EULA 3.0), assinatura mensal com 3 dias de aviso e 5 de carência, mesmo `AppId` no instalador (atualiza a instalação antiga).
* Funções que eram úteis e reais: limpeza de temporários/Windows Update/logs/Lixeira, plano de energia, efeitos visuais, otimizar unidades, CHKDSK, status do firewall/Defender, Modo de Jogo.

## Pontos de atenção para você
* **Clientes com a chave master antiga** precisam de uma chave nova (`GeradorLicencas gerar`).
* O certificado `OtimizadorPRO.pfx` estava dentro do pacote: gere outro antes de assinar o próximo executável.
* O texto dos Termos (seção 5) diz que o app "pode coletar dados"; o app não coleta nada. Ajuste se quiser (Resources/Termos.txt e `AppInfo.TermsVersion` para pedir novo aceite).
* Validei a compilação (0 erros, 0 avisos) e executei a interface em ambiente de teste; recomendo um teste final em um PC Windows real antes de distribuir.
