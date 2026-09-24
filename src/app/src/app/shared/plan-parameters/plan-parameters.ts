import { TrainingPlanItemView } from '../../core/training/training-plan.models';
import { IconName } from '../icons/icon';

/** The five numbers a trainer may prescribe for one exercise (FR-015, S-15). */
export type PlanParameterKey = 'sets' | 'reps' | 'weightKg' | 'durationSeconds' | 'restSeconds';

export interface PlanParameter {
  readonly key: PlanParameterKey;
  readonly icon: IconName;
  readonly label: string;
  readonly unit: 'kg' | 's' | null;
}

/**
 * How each prescribed number is named and drawn — ONE table read by both the trainer's plan builder
 * and the member's plan card, so "Serie" cannot carry one icon where it is typed and another where
 * it is read. One meaning, one icon: sets and reps used to share `repeat`, which made the two
 * numbers side by side on the card indistinguishable without their words.
 *
 * The order is the card's order and the builder's: what to do (sets × reps), with what (load or
 * time), then the rest between.
 */
export const PLAN_PARAMETERS: readonly PlanParameter[] = [
  { key: 'sets', icon: 'sets', label: 'Serie', unit: null },
  { key: 'reps', icon: 'repeat', label: 'Powtórzenia', unit: null },
  { key: 'weightKg', icon: 'hantle', label: 'Ciężar', unit: 'kg' },
  { key: 'durationSeconds', icon: 'time', label: 'Czas', unit: 's' },
  { key: 'restSeconds', icon: 'clock', label: 'Przerwa', unit: 's' },
];

export const PLAN_PARAMETER = Object.fromEntries(
  PLAN_PARAMETERS.map((parameter) => [parameter.key, parameter]),
) as Record<PlanParameterKey, PlanParameter>;

/** A form label: the word, and the unit the number is typed in — "Ciężar (kg)". */
export function parameterFieldLabel(parameter: PlanParameter): string {
  return parameter.unit ? `${parameter.label} (${parameter.unit})` : parameter.label;
}

export interface PrescribedParameter {
  readonly parameter: PlanParameter;
  /** The number as the card shows it, unit included — "60 kg", "8-12". */
  readonly value: string;
}

/**
 * The parameters one item actually carries, in table order. An absent one is OMITTED rather than
 * shown as a dash: a trainer may prescribe a bare exercise, and empty tiles would make it look
 * broken. Reading the table rather than naming the fields is also what keeps a duration-only plank
 * (S-15) from rendering a card with nothing under its name.
 */
export function prescribedParameters(item: TrainingPlanItemView): PrescribedParameter[] {
  return PLAN_PARAMETERS.flatMap((parameter) => {
    const value = item[parameter.key];

    return value === null
      ? []
      : [{ parameter, value: parameter.unit ? `${value} ${parameter.unit}` : String(value) }];
  });
}
