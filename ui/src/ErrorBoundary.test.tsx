import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ErrorBoundary } from './ErrorBoundary';

let broken = true;
function Bomb() {
  if (broken) throw new Error('boom');
  return <p>ok</p>;
}

describe('ErrorBoundary', () => {
  it('shows a notice instead of a blank screen and recovers on retry', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    render(
      <ErrorBoundary label="Problema" retry="Riprova">
        <Bomb />
      </ErrorBoundary>,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('Problema');

    broken = false;
    await userEvent.click(screen.getByRole('button', { name: 'Riprova' }));
    expect(screen.getByText('ok')).toBeInTheDocument();
  });

  it('offers a reload button when given one', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    const reload = vi.fn();
    vi.stubGlobal('location', { ...window.location, reload });
    broken = true;
    render(
      <ErrorBoundary label="Problema" retry="Riprova" reload="Ricarica">
        <Bomb />
      </ErrorBoundary>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Ricarica' }));
    expect(reload).toHaveBeenCalled();
    vi.unstubAllGlobals();
  });
});
