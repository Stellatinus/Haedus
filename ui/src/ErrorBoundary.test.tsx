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
});
