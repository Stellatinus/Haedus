import { act, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { App } from './App';
import { BridgeClient } from './bridge/client';
import { createMockTransport, type MockOptions } from './bridge/mockTransport';
import type { TranscriptionResult } from './bridge/protocol';
import { Editor } from './editor/Editor';
import { I18nProvider } from './i18n/i18n';
import { previewHistory } from './preview/seed';

const first: TranscriptionResult = {
  ...previewHistory[0],
  audioFileName: 'errori.mp3',
  segments: [{ start: 0, end: 4, text: 'Non so perchè è andata così.' }],
  text: 'Non so perchè è andata così.',
};
const second: TranscriptionResult = {
  ...previewHistory[0],
  audioFileName: 'altro.mp3',
  segments: [{ start: 0, end: 4, text: 'Un altro testo.' }],
  text: 'Un altro testo.',
};

async function setup(options: MockOptions = {}) {
  const inner = createMockTransport({ tick: 1, uiLanguage: 'it', installed: ['base'], history: [first, second], ...options });
  const handlers = new Set<(message: string) => void>();
  const client = new BridgeClient({
    send: (m) => inner.send(m),
    onMessage: (h) => {
      handlers.add(h);
      const off = inner.onMessage(h);
      return () => {
        handlers.delete(h);
        off();
      };
    },
  });
  const user = userEvent.setup();
  render(<App client={client} />);
  const recent = await screen.findByRole('list', { name: 'Recenti' });
  await user.click(within(recent).getByRole('button', { name: /errori\.mp3/ }));
  await screen.findByRole('heading', { name: 'Trascrizione di errori.mp3' });
  const emit = (event: string, payload: unknown) => act(() => handlers.forEach((h) => h(JSON.stringify({ v: 1, type: 'event', event, payload }))));
  return { user, emit };
}

async function makeDirty(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]);
  await user.keyboard('{Control>}a{/Control}Ciao');
  await user.tab();
  await screen.findByText('Modifiche non salvate');
}

const dialog = () => screen.findByRole('dialog', { name: 'Modifiche non salvate' });

describe('Modifiche non salvate (#82)', () => {
  it('cambiando scheda chiede Salva / Scarta / Annulla e Annulla resta dov’è', async () => {
    const { user } = await setup();
    await makeDirty(user);

    await user.click(screen.getByRole('button', { name: 'Modelli' }));
    const d = await dialog();
    expect(within(d).getByRole('button', { name: 'Salva' })).toBeInTheDocument();
    expect(within(d).getByRole('button', { name: 'Scarta' })).toBeInTheDocument();
    await user.click(within(d).getByRole('button', { name: 'Annulla' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]).toHaveTextContent('Ciao');
  });

  it('Scarta prosegue e perde le modifiche', async () => {
    const { user } = await setup();
    await makeDirty(user);
    await user.click(screen.getByRole('button', { name: 'Modelli' }));
    await user.click(within(await dialog()).getByRole('button', { name: 'Scarta' }));

    expect(await screen.findByRole('heading', { name: 'Modelli' })).toBeInTheDocument();
    expect(screen.queryByText('Modifiche non salvate')).not.toBeInTheDocument();
  });

  it('Salva scrive nella cronologia e poi prosegue verso l’altro file', async () => {
    const { user } = await setup();
    await makeDirty(user);
    const recent = screen.getByRole('list', { name: 'Recenti' });
    await user.click(within(recent).getByRole('button', { name: /altro\.mp3/ }));
    await user.click(within(await dialog()).getByRole('button', { name: 'Salva' }));

    expect(await screen.findByRole('heading', { name: 'Trascrizione di altro.mp3' })).toBeInTheDocument();
    await user.click(within(screen.getByRole('list', { name: 'Recenti' })).getByRole('button', { name: /errori\.mp3/ }));
    await screen.findByRole('heading', { name: 'Trascrizione di errori.mp3' });
    expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]).toHaveTextContent('Ciao');
  });

  it('«Nuova trascrizione» con modifiche non salvate chiede conferma', async () => {
    const { user } = await setup();
    await makeDirty(user);
    await user.click(screen.getAllByRole('button', { name: /Nuova trascrizione/ })[0]);
    await user.click(within(await dialog()).getByRole('button', { name: 'Scarta' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('region', { name: /Trascrizione di errori\.mp3/ })).not.toBeInTheDocument();
  });

  it('un transcription.completed non sostituisce l’editor sporco e avvisa con un toast', async () => {
    const { user, emit } = await setup();
    await makeDirty(user);

    await emit('transcription.completed', { jobId: 'job-x', result: second, historyId: 'h-x' });

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Trascrizione di errori.mp3' })).toBeInTheDocument();
    expect(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]).toHaveTextContent('Ciao');
    expect(await screen.findByText(/altro\.mp3.*pronta/)).toBeInTheDocument();
  });

  it('un transcription.completed apre il risultato se l’editor è pulito', async () => {
    const { emit } = await setup();
    await emit('transcription.completed', { jobId: 'job-x', result: second, historyId: 'h-x' });
    expect(await screen.findByRole('heading', { name: 'Trascrizione di altro.mp3' })).toBeInTheDocument();
  });

  it('beforeunload chiede conferma solo con modifiche non salvate', async () => {
    const { user } = await setup();
    const clean = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(clean);
    expect(clean.defaultPrevented).toBe(false);

    await makeDirty(user);
    const dirty = new Event('beforeunload', { cancelable: true });
    window.dispatchEvent(dirty);
    expect(dirty.defaultPrevented).toBe(true);
  });

  it('l’Editor azzera onDirtyChange quando si smonta', async () => {
    const onDirtyChange = vi.fn();
    const user = userEvent.setup();
    const view = render(
      <I18nProvider locale="it">
        <Editor result={first} title="errori.mp3" onExport={() => {}} onDirtyChange={onDirtyChange} onCopied={() => {}} />
      </I18nProvider>,
    );
    await user.click(screen.getAllByRole('textbox', { name: /^Testo del segmento/ })[0]);
    await user.keyboard('{Control>}a{/Control}Ciao');
    await user.tab();
    expect(onDirtyChange).toHaveBeenLastCalledWith(true);
    view.unmount();
    expect(onDirtyChange).toHaveBeenLastCalledWith(false);
  });
});
