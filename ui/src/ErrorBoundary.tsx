import { Component, type ErrorInfo, type ReactNode } from 'react';

type Props = { children: ReactNode; label: string; retry: string; reload?: string };
type State = { failed: boolean };

/**
 * Un errore di rendering non deve lasciare la finestra bianca o chiudere l'app (issue #59):
 * si mostra un avviso con un pulsante per riprovare, il resto dell'app resta com'era.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false };

  static getDerivedStateFromError(): State {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Errore di rendering', error, info.componentStack);
  }

  render() {
    if (!this.state.failed) return this.props.children;
    return (
      <div role="alert" className="error-boundary">
        <p>{this.props.label}</p>
        <button type="button" onClick={() => this.setState({ failed: false })}>
          {this.props.retry}
        </button>
        {this.props.reload && (
          <button type="button" onClick={() => window.location.reload()}>
            {this.props.reload}
          </button>
        )}
      </div>
    );
  }
}
