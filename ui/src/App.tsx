import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import type { BridgeClient } from './bridge/client';
import type { ExportFormat } from './bridge/protocol';
import { ConfirmDialog } from './components/ConfirmDialog';
import { ErrorBoundary } from './ErrorBoundary';
import { FileList, type Selection } from './components/FileList';
import { HistoryPanel } from './components/HistoryPanel';
import { Icon, type IconName } from './components/Icon';
import { ModelsPanel } from './components/ModelsPanel';
import { SettingsPanel } from './components/SettingsPanel';
import { StartPage } from './components/StartPage';
import { Workbench } from './components/Workbench';
import { I18nProvider, resolveLocale, useI18n } from './i18n/i18n';
import { useHaedus, type HaedusState } from './state/useHaedus';

type Tab = 'home' | 'history' | 'models' | 'settings';

export function App({ client }: { client: BridgeClient }) {
  const app = useHaedus(client);
  const locale = resolveLocale(app.settings?.uiLanguage);
  const theme = app.settings?.theme;
  const palette = app.settings?.palette;

  useEffect(() => {
    document.documentElement.lang = locale;
  }, [locale]);

  // Tema scelto nelle impostazioni o con il pulsante in alto; senza attributo vale quello del sistema (prefers-color-scheme).
  useEffect(() => {
    const root = document.documentElement;
    if (theme) root.dataset.theme = theme;
    else delete root.dataset.theme;
  }, [theme]);

  // Tema di colore (Sabbia, Natale, Halloween, Ciliegia): senza attributo vale l'aspetto predefinito.
  useEffect(() => {
    const root = document.documentElement;
    if (palette) root.dataset.palette = palette;
    else delete root.dataset.palette;
  }, [palette]);

  return (
    <I18nProvider locale={locale}>
      <RootBoundary>
        <Shell app={app} />
      </RootBoundary>
    </I18nProvider>
  );
}

/** Ultima rete di sicurezza (issue #79): un errore fuori dal banco di lavoro non lascia la finestra bianca. */
function RootBoundary({ children }: { children: ReactNode }) {
  const { t } = useI18n();
  return (
    <ErrorBoundary label={t('renderError')} retry={t('renderRetry')} reload={t('renderReload')}>
      {children}
    </ErrorBoundary>
  );
}

const systemPrefersDark = () => typeof window.matchMedia === 'function' && window.matchMedia('(prefers-color-scheme: dark)').matches;

function Shell({ app }: { app: HaedusState }) {
  const { t, describeError } = useI18n();
  const [tab, setTab] = useState<Tab>('home');
  const [selection, setSelection] = useState<Selection>(null);
  const [language, setLanguage] = useState(app.settings?.language ?? 'auto');
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<string>();
  // Azione di navigazione in attesa della risposta al dialogo «modifiche non salvate» (issue #82).
  const [pendingNav, setPendingNav] = useState<() => void>();
  const saveHandle = useRef<(() => Promise<boolean>) | null>(null);
  const [savingNav, setSavingNav] = useState(false);
  const error = app.error ? describeError(app.error.code) : undefined;

  useEffect(() => {
    if (app.settings?.language) setLanguage(app.settings.language);
  }, [app.settings?.language]);

  // Il modello consigliato dal Test bench dipende dalla lingua scelta qui.
  const { setRecommendLanguage } = app;
  useEffect(() => {
    setRecommendLanguage(language);
  }, [language, setRecommendLanguage]);

  // Una trascrizione appena conclusa si apre da sola; quelle della coda restano a chi le sceglie nell'elenco.
  useEffect(() => {
    if (app.open?.jobId) setSelection({ kind: 'open' });
  }, [app.open]);

  const running = Boolean(app.jobId) || app.progress?.status === 'queued' || app.queueRunning;
  const paused = running && app.progress?.status === 'paused';
  const needsLanguage = Boolean(app.activeModel?.requiresLanguage) && language === 'auto';
  const editing = tab === 'home' && selection !== null && Boolean(app.open);
  const unsaved = editing && dirty;

  const { reportEditorDirty } = app;
  useEffect(() => {
    reportEditorDirty(unsaved);
  }, [unsaved, reportEditorDirty]);

  // Chiudere o ricaricare la pagina nel browser con correzioni non salvate chiede conferma.
  useEffect(() => {
    if (!unsaved) return;
    const warn = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', warn);
    return () => window.removeEventListener('beforeunload', warn);
  }, [unsaved]);

  /** Esegue la navigazione, o prima chiede cosa fare delle correzioni non salvate. */
  const guarded = (action: () => void) => {
    if (unsaved) setPendingNav(() => action);
    else action();
  };

  const tabs: { id: Tab; label: string; icon: IconName }[] = [
    { id: 'home', label: t('tabHome'), icon: 'home' },
    { id: 'history', label: t('tabHistory'), icon: 'history' },
    { id: 'models', label: t('tabModels'), icon: 'models' },
    { id: 'settings', label: t('tabSettings'), icon: 'settings' },
  ];

  const pick = async () => {
    await app.pickAudio();
    setSelection(null);
  };

  const resolveNav = async (choice: 'save' | 'discard' | 'cancel') => {
    const action = pendingNav;
    if (choice === 'cancel' || !action) {
      setPendingNav(undefined);
      return;
    }
    if (choice === 'save') {
      setSavingNav(true);
      const ok = await saveHandle.current?.();
      setSavingNav(false);
      if (!ok) return; // l'errore compare nel banner; il dialogo resta per scegliere di nuovo
    }
    setPendingNav(undefined);
    action();
  };

  const effectiveTheme = app.settings?.theme ?? (systemPrefersDark() ? 'dark' : 'light');
  const toggleTheme = () => app.settings && void app.saveSettings({ ...app.settings, theme: effectiveTheme === 'dark' ? 'light' : 'dark' });

  const showToast = useCallback((message: string) => {
    setToast(message);
    setTimeout(() => setToast(undefined), 2200);
  }, []);

  // Una trascrizione conclusa mentre si corregge un'altra non la sostituisce: la scelta resta all'utente.
  const { deferred } = app;
  useEffect(() => {
    if (deferred) showToast(t('completedWhileEditing', { file: deferred.file }));
  }, [deferred, showToast, t]);

  return (
    <div className="app">
      <header className="nav">
        <div className="brand" title={t('localBadgeTitle')}>
          <i aria-hidden="true">C</i>Haedus
        </div>
        <nav className="nav__tabs" aria-label={t('sections')}>
          {tabs.map((item) => (
            <button key={item.id} type="button" className={`tab${tab === item.id ? ' on' : ''}`} aria-current={tab === item.id ? 'page' : undefined} onClick={() => guarded(() => setTab(item.id))}>
              <Icon name={item.icon} />
              <span>{item.label}</span>
            </button>
          ))}
        </nav>
        <div className="sp" />
        <div className="save" role="status" title={t('localBadgeTitle')}>
          <span className={`dot${editing && dirty ? ' warn' : ''}`} />
          <span>{editing && dirty ? t('saveEdited') : editing && app.open?.historyId ? t('saveHistory') : t('localBadge')}</span>
        </div>
        <button type="button" className="btn icon" title={t('toggleTheme')} aria-label={t('toggleTheme')} onClick={toggleTheme}>
          <Icon name={effectiveTheme === 'dark' ? 'sun' : 'moon'} />
        </button>
        <ExportMenu app={app} dirty={editing && dirty} />
      </header>

      <div className="body">
        {tab === 'home' && (
          <FileList
            app={app}
            selection={selection}
            running={running}
            paused={paused}
            onSelectQueue={(item) =>
              guarded(() => {
                if (item.status === 'done') void app.openQueueItem(item.key);
                setSelection({ kind: 'queue', key: item.key });
              })
            }
            onSelectRecent={(id) =>
              guarded(() => {
                void app.openFromHistory(id);
                setSelection({ kind: 'recent', id });
              })
            }
            onNew={() => guarded(() => setSelection(null))}
            onStartQueue={() => guarded(() => void app.startQueue(language))}
            startDisabled={needsLanguage}
            onAddFiles={() =>
              guarded(() => {
                void app.addToQueue();
                setSelection(null);
              })
            }
          />
        )}

        <main className="main">
          {error && (
            <div role="alert" className="banner banner--error">
              <div>
                <strong>{error.title}</strong> {error.action}
                <span className="banner__code">
                  {' '}
                  ({t('errorCode')}: {app.error!.code})
                </span>
              </div>
              {app.error!.code === 'history_folder_blocked' && app.desktop && (
                <button type="button" className="link" onClick={() => setTab('settings')}>
                  {t('changeFolder')}
                </button>
              )}
              <button type="button" className="link" onClick={app.dismissError}>
                {t('close')}
              </button>
            </div>
          )}
          {app.notice && (
            <div role="status" className="banner banner--info">
              <span>{t(app.notice.key, app.notice.params)}</span>
              <button type="button" className="link" onClick={app.dismissNotice}>
                {t('close')}
              </button>
            </div>
          )}

          {tab === 'home' &&
            (selection === null ? (
              app.readyModels.length === 0 ? (
                <section className="page" aria-labelledby="transcribe-title">
                  <h2 id="transcribe-title" className="section-title">
                    {t('newTranscription')}
                  </h2>
                  <div className="card callout">
                    <p>{t('needModel')}</p>
                    <button type="button" className="btn pri" onClick={() => setTab('models')}>
                      {t('chooseModel')}
                    </button>
                  </div>
                </section>
              ) : (
                <StartPage
                  app={app}
                  language={language}
                  onLanguage={setLanguage}
                  needsLanguage={needsLanguage}
                  running={running}
                  onPick={() => guarded(() => void pick())}
                  onOpenModels={() => setTab('models')}
                  onStart={() => void app.start(language)}
                  onStartQueue={() => app.startQueue(language)}
                />
              )
            ) : (
              <ErrorBoundary key={selection === null ? 'none' : JSON.stringify(selection)} label={t('renderError')} retry={t('renderRetry')}>
                <Workbench app={app} selection={selection} onDirtyChange={setDirty} saveHandle={saveHandle} onCopied={() => showToast(t('copied'))} />
              </ErrorBoundary>
            ))}
          {tab === 'history' && (
            <HistoryPanel
              app={app}
              onOpened={(id) => {
                setSelection({ kind: 'recent', id });
                setTab('home');
              }}
            />
          )}
          {tab === 'models' && <ModelsPanel app={app} />}
          {tab === 'settings' && <SettingsPanel app={app} />}
        </main>
      </div>
      {pendingNav && (
        <ConfirmDialog title={t('unsavedTitle')} confirmLabel={t('save')} onConfirm={() => void resolveNav('save')} onCancel={() => void resolveNav('cancel')}>
          <p>{t('unsavedBody')}</p>
          <button type="button" className="danger" disabled={savingNav} onClick={() => void resolveNav('discard')}>
            {t('unsavedDiscard')}
          </button>
        </ConfirmDialog>
      )}
      {toast && (
        <div className="toast" role="status">
          {toast}
        </div>
      )}
    </div>
  );
}

function ExportMenu({ app, dirty }: { app: HaedusState; dirty: boolean }) {
  const { t } = useI18n();
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const formats: ExportFormat[] = ['txt', 'srt', 'vtt'];

  useEffect(() => {
    if (!open) return;
    const close = (e: Event) => {
      if (e instanceof KeyboardEvent ? e.key === 'Escape' : !root.current?.contains(e.target as Node)) setOpen(false);
    };
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', close);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', close);
    };
  }, [open]);

  return (
    <div className="exportmenu" ref={root}>
      <button type="button" className="btn pri" aria-haspopup="menu" aria-expanded={open} disabled={!app.open} title={app.open ? undefined : t('exportNothing')} onClick={() => setOpen(!open)}>
        <Icon name="upload" />
        {t('export')}
      </button>
      {open && (
        <div className="popmenu" role="menu">
          {formats.map((format) => (
            <button
              key={format}
              type="button"
              role="menuitem"
              onClick={() => {
                setOpen(false);
                void app.exportAs(format);
              }}
            >
              {t('exportAs', { format: format.toUpperCase() })}
            </button>
          ))}
          {dirty && <p className="note popmenu__note">{t('exportOriginal')}</p>}
        </div>
      )}
    </div>
  );
}
