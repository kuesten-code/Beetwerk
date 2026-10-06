import { createContext, useCallback, useContext, useRef, useState, type ReactNode } from "react";

interface ToastAction {
  label: string;
  run: () => void;
}

interface ToastState {
  id: number;
  message: string;
  error: boolean;
  action?: ToastAction;
}

interface ToastApi {
  show: (message: string, action?: ToastAction) => void;
  error: (message: string) => void;
}

const ToastContext = createContext<ToastApi | null>(null);

export function useToast(): ToastApi {
  const value = useContext(ToastContext);
  if (!value) throw new Error("useToast außerhalb von ToastProvider");
  return value;
}

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toast, setToast] = useState<ToastState | null>(null);
  const timer = useRef<number | undefined>(undefined);

  const present = useCallback((next: Omit<ToastState, "id">) => {
    window.clearTimeout(timer.current);
    const id = Date.now();
    setToast({ ...next, id });
    timer.current = window.setTimeout(() => setToast((current) => (current?.id === id ? null : current)), next.action ? 6000 : 3500);
  }, []);

  const [api] = useState<ToastApi>(() => ({
    show: (message, action) => present({ message, action, error: false }),
    error: (message) => present({ message, error: true }),
  }));

  return (
    <ToastContext.Provider value={api}>
      {children}
      {toast && (
        <div className={`toast${toast.error ? " error" : ""}`} role="status" aria-live="polite">
          <span>{toast.message}</span>
          {toast.action && (
            <button
              type="button"
              className="link"
              onClick={() => {
                toast.action!.run();
                setToast(null);
              }}
            >
              {toast.action.label}
            </button>
          )}
        </div>
      )}
    </ToastContext.Provider>
  );
}
